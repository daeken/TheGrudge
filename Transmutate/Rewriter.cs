using System;
using System.Collections.Generic;
using System.Diagnostics;
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
					Add($"Label{label.Value.Num}: ;");
			}
			void Add(string stmt) => code += new string('\t', depth) + stmt + (stmt.EndsWith(":") ? "\n" : ";\n");
			void Push(string expr) => stack.Push(expr);
			string Pop() => stack.Pop();
			
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
					case Instruction<WasmType> inst when inst == Opcode.loop:
						Add($"Label{++labelNum}:");
						StartBlock("", (true, labelNum));
						break;
					case Instruction<WasmType> inst when inst == Opcode.block:
						StartBlock("", (false, ++labelNum));
						break;
					case Instruction<WasmType> inst when inst == Opcode.@if: StartBlock($"if(({Pop()}) != 0)"); break;
					case Instruction<uint> inst when inst == Opcode.call: {
						var (fn, ft) = GetFunction(inst.Operand);
						var p = ft.ParamTypes.Select(_ => Pop()).ToList();
						var c = $"{fn}({string.Join(", ", p)})";
						if(ft.ReturnType == null)
							Add(c);
						else
							Push(c);
						break;
					}
					case Instruction<uint> inst when inst == Opcode.call_indirect: {
						var ft = Module.FuncTypes[inst.Operand];
						var p = ft.ParamTypes.Select(_ => Pop()).ToList();
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
							case Opcode.i64_store: Swap(); Add($"__Storei64({Pop()}, {Pop()})"); break;
							case Opcode.i64_load: Push($"__Loadi64({Pop()})"); break;
							case Opcode.i32_store8: Swap(); Add($"__Storei32_8({Pop()}, {Pop()})"); break;
							case Opcode.i32_load8_s: Push($"__Loadi32_8({Pop()})"); break;
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

							case Opcode.i32_shr_u: {
								var (b, a) = (Pop(), Pop());
								Push($"(int) ((uint) ({a}) >> ({b}))");
								break;
							}

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
								Binary("<<");
								break;
							
							case Opcode.i32_shr_s:
							case Opcode.i64_shr_s:
								Binary(">>");
								break;
							
							case Opcode.i32_ge_s:
							case Opcode.i64_ge_s:
								Binary(">=");
								ToBool();
								break;

							case Opcode.i32_gt_s:
							case Opcode.i64_gt_s:
								Binary(">");
								ToBool();
								break;

							case Opcode.i32_le_s:
							case Opcode.i64_le_s:
								Binary("<=");
								ToBool();
								break;

							case Opcode.i32_lt_s:
							case Opcode.i64_lt_s:
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
							
							case Opcode.@else: EndBlock(); StartBlock("else"); break;
							
							case Opcode.end: EndBlock(); break;
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
				return (func.FieldName, (FuncType) func.Type);
			}

			var sig = Module.FunctionSignatures[(int) num - fimp.Count];
			var global = Module.Exports.FirstOrDefault(x => x.Kind == ExternalKind.Function && x.Index == num);
			return (global != null ? global.FieldName : $"F{num - fimp.Count}", sig);
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