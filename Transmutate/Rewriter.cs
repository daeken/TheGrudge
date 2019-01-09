using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using MoreLinq.Extensions;
using PrettyPrinter;
using WasmReader;

namespace Transmutate {
	public class Rewriter {
		readonly WasmModule Module;
		readonly string Namespace, Class;
		
		public Rewriter(WasmModule module, string @namespace, string @class) {
			Module = module;
			Namespace = @namespace;
			Class = @class;
		}

		public string WriteCode() {
			var code = "using static WasmBootstrap.Module;\n";
			code += $"namespace {Namespace} {{\n";
			code += $"\tpublic static class {Class} {{\n";

			var gimp = Module.Imports.Count(x => x.Kind == ExternalKind.Global);
			Module.Globals.ForEach((global, i) =>
				code += $"\t\tpublic static {Rewrite(global.Type)} {GlobalName((uint) (i + gimp))};\n");
			if(Module.Globals.Length != 0)
				code += "\n";

			code += $"\t\tstatic {Class}() {{\n";
			Module.Globals.ForEach((global, i) =>
				code += $"\t\t\t{GlobalName((uint) (i + gimp))} = {EvaluateInit(global.InitExpr)};\n");
			foreach(var element in Module.Elements) {
				var table = Module.Imports.Where(x => x.Kind == ExternalKind.Table).Skip((int) element.Index).First().FieldName;
				var elems = element.Elems.Select(x => {
					var (name, type) = GetFunction(x);
					return $"({Rewrite(type)}) {name}";
				});
				code += $"\t\t\t{table} = new object[] {{ {string.Join(", ", elems)} }};\n";
			}

			if(Module.Exports.Count(x => x.FieldName == "__post_instantiate") == 1)
				code += "\t\t\t__post_instantiate();\n";
			code += "\t\t}\n\n";
			
			var fimp = Module.Imports.Count(x => x.Kind == ExternalKind.Function);
			Module.Functions.ForEach((func, i) => code += Rewrite(i + fimp, func));
			code += "\t}\n";
			code += "}";
			Console.WriteLine(code);
			return code;
		}

		string EvaluateInit(List<Instruction> init) {
			Debug.Assert(init.Count == 1);
			switch(init[0]) {
				case Instruction<int> inst when inst == Opcode.i32_const: return inst.Operand.ToString();
				case Instruction<float> inst when inst == Opcode.f32_const: return inst.Operand.ToString();
				case Instruction<double> inst when inst == Opcode.f64_const: return inst.Operand.ToString();
				case Instruction<uint> inst when inst == Opcode.get_global: return GlobalName(inst.Operand);
			}
			throw new NotSupportedException(init.ToPrettyString());
		}

		string Rewrite(int fnum, WasmFunction func) {
			var code = "";
			var depth = 2;
			var stack = new Stack<string>();
			var labelStack = new Stack<(bool Added, int Num)?>();
			var labelNum = 0;
			
			void StartBlock(string head, (bool Added, int Num)? label = null) {
				code += new string('\t', depth++) + head + " {\n";
				labelStack.Push(label);
			}
			void EndBlock() {
				code += new string('\t', --depth) + "}\n";
				var label = labelStack.Pop();
				if(label != null && !label.Value.Added)
					Add($"Label{label.Value.Num}:");
			}
			void Add(string stmt) => code += new string('\t', depth) + stmt + (/*stmt.EndsWith(":") ? "\n" :*/ ";\n");
			void Push(string expr) => stack.Push(expr);
			string Pop() => stack.Pop();

			string LastLine() => code.TrimEnd().Split("\n").Last();
			void CutLastLine() => code = code.Substring(0, code.Length - LastLine().Length - 1);
			
			var fname = GetFunction((uint) fnum).Item1;

			StartBlock($"public unsafe static {Rewrite(func.Signature.ReturnType)} {fname}({string.Join(", ", func.Signature.ParamTypes.Select((x, i) => $"{Rewrite(x)} p{i}"))})");
			StartBlock("unchecked");
			
			string LocalName(uint num) =>
				num < func.Signature.ParamTypes.Length
					? $"p{num}"
					: $"l{num - func.Signature.ParamTypes.Length}";

			for(var i = 0; i < func.Locals.Length; ++i)
				Add($"{Rewrite(func.Locals[i])} l{i} = 0");

			void Binary(string op) {
				var (b, a) = (Pop(), Pop());
				Push($"({a}) {op} ({b})");
			}
			void UnsignedBinary(string op, string type) {
				var (b, a) = (Pop(), Pop());
				Push($"({type}) ((u{type}) ({a}) {op} (u{type}) ({b}))");
			}

			void ToBool() => Push($"({Pop()}) ? 1 : 0");

			void Swap() {
				var (b, a) = (Pop(), Pop());
				Push(b);
				Push(a);
			}

			foreach(var _inst in func.Instructions) {
				switch(_inst) {
					case Instruction<uint> inst when inst == Opcode.get_global: Push(GlobalName(inst.Operand)); break;
					case Instruction<uint> inst when inst == Opcode.set_global: Add($"{GlobalName(inst.Operand)} = {Pop()}"); break;
					case Instruction<uint> inst when inst == Opcode.get_local: Push(LocalName(inst.Operand)); break;
					case Instruction<uint> inst when inst == Opcode.set_local: Add($"{LocalName(inst.Operand)} = {Pop()}"); break;
					case Instruction<uint> inst when inst == Opcode.tee_local: Push($"({LocalName(inst.Operand)} = {Pop()})"); break;
					case Instruction<int> inst when inst == Opcode.i32_const: Push(inst.Operand.ToString()); break;
					case Instruction<long> inst when inst == Opcode.i64_const: Push(inst.Operand + "L"); break;
					case Instruction<float> inst when inst == Opcode.f32_const:
						Push($"Reinterpret_f32(0x{BitConverter.ToUInt32(BitConverter.GetBytes(inst.Operand)):X}U)");
						break;
					case Instruction<double> inst when inst == Opcode.f64_const:
						Push($"Reinterpret_f64(0x{BitConverter.ToUInt64(BitConverter.GetBytes(inst.Operand)):X}UL)");
						break;
					case Instruction<WasmType> inst when inst == Opcode.loop:
						Add($"Label{++labelNum}:");
						StartBlock("", (true, labelNum));
						break;
					case Instruction<WasmType> inst when inst == Opcode.block:
						StartBlock("", (false, ++labelNum));
						break;
					case Instruction<WasmType> inst when inst == Opcode.@if:
						StartBlock($"if(({Pop()}) != 0)");
						Push("~IF~");
						break;
					case Instruction<uint> inst when inst == Opcode.call: {
						var (fn, ft) = GetFunction(inst.Operand);
						var p = ft.ParamTypes.Select(_ => Pop()).Reverse().ToList();
						var c = $"{fn}({string.Join(", ", p)})";
						if(ft.ReturnType == null)
							Add(c);
						else
							Push(c);
						break;
					}
					case Instruction<uint> inst when inst == Opcode.call_indirect: {
						var ft = Module.FuncTypes[inst.Operand];
						var p = ft.ParamTypes.Select(_ => Pop()).Reverse().ToList();
						//var c = $"{fn}({string.Join(", ", p)})";
						Add("throw new System.NotImplementedException(\"Indirect\")");
						if(ft.ReturnType != null)
							Push("0");
						break;
					}
					case Instruction<(uint Alignment, uint Offset)> inst:
						switch(inst.Op) {
							case Opcode.i32_store: Swap(); Add($"__Storei32({Pop()}, {Pop()})"); break;
							case Opcode.i32_load: Push($"__Loadi32({Pop()})"); break;
							case Opcode.i32_store8: Swap(); Add($"__Storei32_8({Pop()}, {Pop()})"); break;
							case Opcode.i32_load8_s: Push($"__Loadi32_8s({Pop()})"); break;
							case Opcode.i32_load8_u: Push($"__Loadi32_8u({Pop()})"); break;
							case Opcode.i32_store16: Swap(); Add($"__Storei32_16({Pop()}, {Pop()})"); break;
							case Opcode.i32_load16_s: Push($"__Loadi32_16s({Pop()})"); break;
							case Opcode.i32_load16_u: Push($"__Loadi32_16u({Pop()})"); break;
							case Opcode.i64_store: Swap(); Add($"__Storei64({Pop()}, {Pop()})"); break;
							case Opcode.i64_load: Push($"__Loadi64({Pop()})"); break;
							case Opcode.i64_store8: Swap(); Add($"__Storei64_8({Pop()}, {Pop()})"); break;
							case Opcode.i64_load8_s: Push($"__Loadi64_8s({Pop()})"); break;
							case Opcode.i64_load8_u: Push($"__Loadi64_8u({Pop()})"); break;
							case Opcode.i64_store16: Swap(); Add($"__Storei64_16({Pop()}, {Pop()})"); break;
							case Opcode.i64_load16_s: Push($"__Loadi64_16s({Pop()})"); break;
							case Opcode.i64_load16_u: Push($"__Loadi64_16u({Pop()})"); break;
							case Opcode.i64_store32: Swap(); Add($"__Storei64_32({Pop()}, {Pop()})"); break;
							case Opcode.i64_load32_s: Push($"__Loadi64_32s({Pop()})"); break;
							case Opcode.i64_load32_u: Push($"__Loadi64_32u({Pop()})"); break;
							case Opcode.f32_store: Swap(); Add($"__Storef32({Pop()}, {Pop()})"); break;
							case Opcode.f32_load: Push($"__Loadf32({Pop()})"); break;
							case Opcode.f64_store: Swap(); Add($"__Storef64({Pop()}, {Pop()})"); break;
							case Opcode.f64_load: Push($"__Loadf64({Pop()})"); break;
							default:
								throw new NotImplementedException($"Unhandled instruction: {_inst.ToPrettyString()}");
						}
						break;
					case Instruction<uint> inst when inst == Opcode.br_if: {
						var labels = labelStack.ToList();
						Add($"if(({Pop()}) != 0) goto Label{labels[(int) inst.Operand].Value.Num}");
						break;
					}
					case Instruction<uint> inst when inst == Opcode.br: {
						var labels = labelStack.ToList();
						Add($"goto Label{labels[(int) inst.Operand].Value.Num}");
						break;
					}
					case Instruction<(uint[] Table, uint Default)> inst when inst == Opcode.br_table: {
						Add("throw new System.Exception(\"br_table\")");
						break;
					}
					default:
						switch(_inst.Op) {
							case Opcode.i32_add:
							case Opcode.i64_add:
							case Opcode.f32_add:
							case Opcode.f64_add:
								Binary("+");
								break;

							case Opcode.i32_sub:
							case Opcode.i64_sub:
							case Opcode.f32_sub:
							case Opcode.f64_sub:
								Binary("-");
								break;

							case Opcode.i32_mul:
							case Opcode.i64_mul:
							case Opcode.f32_mul:
							case Opcode.f64_mul:
								Binary("*");
								break;

							case Opcode.i32_div_s:
							case Opcode.i64_div_s:
							case Opcode.f32_div:
							case Opcode.f64_div:
								Binary("/");
								break;

							case Opcode.i32_div_u:
								UnsignedBinary("/", "int");
								break;
							case Opcode.i64_div_u:
								UnsignedBinary("/", "long");
								break;

							case Opcode.i32_rem_s:
							case Opcode.i64_rem_s:
								Binary("%");
								break;
							
							case Opcode.i32_rem_u:
								UnsignedBinary("%", "int");
								break;
							case Opcode.i64_rem_u:
								UnsignedBinary("%", "long");
								break;
							
							case Opcode.f32_neg:
							case Opcode.f64_neg:
								Push($"-({Pop()})");
								break;
							
							case Opcode.f32_abs:
								Push($"System.MathF.Abs({Pop()})");
								break;
							case Opcode.f64_abs:
								Push($"System.Math.Abs({Pop()})");
								break;
							
							case Opcode.f32_sqrt:
								Push($"System.MathF.Sqrt({Pop()})");
								break;
							case Opcode.f64_sqrt:
								Push($"System.Math.Sqrt({Pop()})");
								break;
							
							case Opcode.f32_floor:
								Push($"System.MathF.Floor({Pop()})");
								break;
							case Opcode.f64_floor:
								Push($"System.Math.Floor({Pop()})");
								break;
							
							case Opcode.f32_ceil:
								Push($"System.MathF.Ceiling({Pop()})");
								break;
							case Opcode.f64_ceil:
								Push($"System.Math.Ceiling({Pop()})");
								break;
							
							case Opcode.i32_and:
							case Opcode.i64_and:
								Binary("&");
								break;
							
							case Opcode.i32_or:
							case Opcode.i64_or:
								Binary("|");
								break;
							
							case Opcode.i32_xor:
							case Opcode.i64_xor:
								Binary("^");
								break;
							
							case Opcode.i32_shl:
							case Opcode.i64_shl:
								Push($"(int) ({Pop()})");
								Binary("<<");
								break;
							
							case Opcode.i32_shr_s:
							case Opcode.i64_shr_s:
								Push($"(int) ({Pop()})");
								Binary(">>");
								break;
							
							case Opcode.i32_shr_u:
								Swap();
								Push($"(uint) ({Pop()})");
								Swap();
								Binary(">>");
								Push($"(int) ({Pop()})");
								break;
							case Opcode.i64_shr_u:
								Push($"(int) ({Pop()})");
								Swap();
								Push($"(ulong) ({Pop()})");
								Swap();
								Binary(">>");
								Push($"(long) ({Pop()})");
								break;

							case Opcode.i32_ge_s:
							case Opcode.i64_ge_s:
							case Opcode.f32_ge:
							case Opcode.f64_ge:
								Binary(">=");
								ToBool();
								break;

							case Opcode.i32_gt_s:
							case Opcode.i64_gt_s:
							case Opcode.f32_gt:
							case Opcode.f64_gt:
								Binary(">");
								ToBool();
								break;

							case Opcode.i32_le_s:
							case Opcode.i64_le_s:
							case Opcode.f32_le:
							case Opcode.f64_le:
								Binary("<=");
								ToBool();
								break;

							case Opcode.i32_lt_s:
							case Opcode.i64_lt_s:
							case Opcode.f32_lt:
							case Opcode.f64_lt:
								Binary("<");
								ToBool();
								break;

							case Opcode.i32_lt_u: {
								var (b, a) = (Pop(), Pop());
								Push($"(uint) ({a}) < (uint) ({b})");
								ToBool();
								break;
							}
							case Opcode.i32_le_u: {
								var (b, a) = (Pop(), Pop());
								Push($"(uint) ({a}) <= (uint) ({b})");
								ToBool();
								break;
							}
							case Opcode.i32_gt_u: {
								var (b, a) = (Pop(), Pop());
								Push($"(uint) ({a}) > (uint) ({b})");
								ToBool();
								break;
							}
							case Opcode.i32_ge_u: {
								var (b, a) = (Pop(), Pop());
								Push($"(uint) ({a}) >= (uint) ({b})");
								ToBool();
								break;
							}
	
							case Opcode.i64_lt_u: {
								var (b, a) = (Pop(), Pop());
								Push($"(ulong) ({a}) < (ulong) ({b})");
								ToBool();
								break;
							}
							case Opcode.i64_le_u: {
								var (b, a) = (Pop(), Pop());
								Push($"(ulong) ({a}) <= (ulong) ({b})");
								ToBool();
								break;
							}
							case Opcode.i64_gt_u: {
								var (b, a) = (Pop(), Pop());
								Push($"(ulong) ({a}) > (ulong) ({b})");
								ToBool();
								break;
							}
							case Opcode.i64_ge_u: {
								var (b, a) = (Pop(), Pop());
								Push($"(ulong) ({a}) >= (ulong) ({b})");
								ToBool();
								break;
							}
	
							case Opcode.i32_eq:
							case Opcode.i64_eq:
							case Opcode.f32_eq:
							case Opcode.f64_eq:
								Binary("==");
								ToBool();
								break;
							
							case Opcode.i32_ne:
							case Opcode.i64_ne:
							case Opcode.f32_ne:
							case Opcode.f64_ne:
								Binary("!=");
								ToBool();
								break;
							
							case Opcode.i32_eqz:
							case Opcode.i64_eqz:
								Push("0");
								Binary("==");
								ToBool();
								break;
							
							case Opcode.f32_convert_s_i32:
							case Opcode.f32_convert_s_i64:
							case Opcode.f32_convert_u_i32:
							case Opcode.f32_convert_u_i64:
							case Opcode.f32_demote_f64:
								Push($"(float) ({Pop()})");
								break;
							case Opcode.f64_convert_s_i32:
							case Opcode.f64_convert_s_i64:
							case Opcode.f64_convert_u_i32:
							case Opcode.f64_convert_u_i64:
							case Opcode.f64_promote_f32:
								Push($"(double) ({Pop()})");
								break;
							
							case Opcode.i32_reinterpret_f32:
								Push($"Reinterpret_i32({Pop()})");
								break;
							case Opcode.f32_reinterpret_i32:
								Push($"Reinterpret_f32({Pop()})");
								break;
							case Opcode.i64_reinterpret_f64:
								Push($"Reinterpret_i64({Pop()})");
								break;
							case Opcode.f64_reinterpret_i64:
								Push($"Reinterpret_f64({Pop()})");
								break;
							
							case Opcode.i32_wrap_i64:
								Push($"(int) ({Pop()})");
								break;
							
							case Opcode.i64_extend_s_i32:
								Push($"(long) ({Pop()})");
								break;
							case Opcode.i64_extend_u_i32:
								Push($"(long) (ulong) (uint) ({Pop()})");
								break;
							
							case Opcode.i32_trunc_s_f32:
								Push($"(int) System.MathF.Truncate({Pop()})");
								break;
							case Opcode.i32_trunc_s_f64:
								Push($"(int) System.Math.Truncate({Pop()})");
								break;
							case Opcode.i32_trunc_u_f32:
								Push($"(int) (uint) System.MathF.Truncate({Pop()})");
								break;
							case Opcode.i32_trunc_u_f64:
								Push($"(int) (uint) System.Math.Truncate({Pop()})");
								break;

							case Opcode.i64_trunc_s_f32:
								Push($"(long) System.MathF.Truncate({Pop()})");
								break;
							case Opcode.i64_trunc_s_f64:
								Push($"(long) System.Math.Truncate({Pop()})");
								break;
							case Opcode.i64_trunc_u_f32:
								Push($"(long) (ulong) System.MathF.Truncate({Pop()})");
								break;
							case Opcode.i64_trunc_u_f64:
								Push($"(long) (ulong) System.Math.Truncate({Pop()})");
								break;

							case Opcode.@else:
								if(stack.Peek() != "~IF~" && LastLine().Contains("if(")) {
									Swap();
									if(stack.Pop() != "~IF~") throw new Exception("Expected only one value from ternary if");
									var last = LastLine().Split("if(")[1];
									Push(last.Substring(0, last.Length - 3));
									CutLastLine();
									Push("~TERNARY~");
								} else {
									if(stack.Pop() != "~IF~") throw new Exception("Expected no new values from non-ternary if");
									EndBlock();
									StartBlock("else");
								}
								break;
							
							case Opcode.end:
								if(stack.Count >= 3) {
									var slist = stack.ToList();
									if(slist[1] != "~TERNARY~") {
										EndBlock();
										break;
									}
									Add($"/* {slist.ToPrettyString()} */");
									labelStack.Pop();
									depth--;
									var _else = Pop();
									if(Pop() != "~TERNARY~") throw new Exception("Expected ternary");
									var cond = Pop();
									var _if = Pop();
									Push($"({cond}) ? ({_if}) : ({_else})");
								} else
									EndBlock();
								break;
							case Opcode.@return:
								Add(func.Signature.ReturnType == null ? "return" : $"return {Pop()}");
								break;
							
							case Opcode.drop:
								stack.Pop();
								break;
							
							case Opcode.unreachable:
								Add("throw new System.Exception(\"Trapping for unreachable code\")");
								break;
							
							case Opcode.grow_memory:
								Push($"growMemory({Pop()})");
								break;
							
							case Opcode.nop:
								break;
							
							default:
								func.Print();
								throw new NotImplementedException($"Unhandled instruction: {_inst.ToPrettyString()}");
						}
						break;
				}
			}
			
			if(func.Signature.ReturnType != null && stack.Count != 0)
				Add($"return {Pop()}");
			
			EndBlock();
			EndBlock();

			return code;
		}

		(string, FuncType) GetFunction(uint num) {
			var fimp = Module.Imports.Where(x => x.Kind == ExternalKind.Function).ToList();
			if(num < fimp.Count) {
				var func = fimp[(int) num];
				return (func.FieldName.Replace(".", "_").Replace("-", "_"), (FuncType) func.Type);
			}

			var sig = Module.FunctionSignatures[(int) num - fimp.Count];
			var global = Module.Exports.FirstOrDefault(x => x.Kind == ExternalKind.Function && x.Index == num);
			return (global != null ? global.FieldName.Replace(".", "_").Replace("-", "_") : $"F{num}", sig);
		}

		string GlobalName(uint num) {
			var fimp = Module.Imports.Where(x => x.Kind == ExternalKind.Global).ToList();
			if(num < fimp.Count)
				return fimp[(int) num].FieldName;
			var gexp = Module.Exports.Where(x => x.Kind == ExternalKind.Global).ToList();
			return num < gexp.Count ? gexp.First(x => x.Index == num).FieldName : $"G{num}";
		}

		string Rewrite(WasmType type) {
			switch(type) {
				case null: return "void";
				case PrimitiveType pt: return pt.IlType.ToString();
				case FuncType ft:
					var ts = ft.ReturnType == null ? "System.Action" : "System.Func";
					if(ft.ParamTypes.Length != 0 || ft.ReturnType != null)
						ts += "<";
					if(ft.ParamTypes.Length != 0) {
						ts += string.Join(", ", ft.ParamTypes.Select(Rewrite));
						if(ft.ReturnType != null) ts += ", ";
					}
					if(ft.ReturnType != null)
						ts += Rewrite(ft.ReturnType) + ">";
					else if(ft.ParamTypes.Length != 0)
						ts += ">";
					return ts;
				default: throw new NotImplementedException("Unknown WasmType for rewriting: " + type.ToPrettyString());
			}
		}
	}
}