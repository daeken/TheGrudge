using System;
using System.Collections.Generic;
using System.Linq;
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
			
			Module.Functions.ForEach((func, i) => code += Rewrite(i, func));
			code += "\t}\n";
			code += "}";
			return code;
		}

		string Rewrite(int fnum, WasmFunction func) {
			var code = "";
			var depth = 2;
			var stack = new Stack<string>();
			
			void StartBlock(string head) => code += new string('\t', depth++) + head + " {\n";
			void EndBlock() => code += new string('\t', --depth) + "}\n";
			void Add(string stmt) => code += new string('\t', depth) + stmt + ";\n";
			void Push(string expr) => stack.Push(expr);
			string Pop() => stack.Pop();

			var ef = Module.Exports.FirstOrDefault(x => x.Kind == ExternalKind.Function && x.Index == fnum);
			var fname = ef != null
				? ef.FieldName
				: $"f{fnum - Module.Exports.Count(x => x.Kind == ExternalKind.Function)}";
			
			if(fname == "__post_instantiate")
				StartBlock($"static {Class}()");
			else
				StartBlock($"public static {Rewrite(func.Signature.ReturnType)} {fname}({string.Join(", ", func.Signature.ParamTypes.Select((x, i) => $"{Rewrite(x)} p{i}"))})");
			
			string LocalName(uint num) =>
				num < func.Signature.ParamTypes.Length
					? $"p{num}"
					: $"l{num - func.Signature.ParamTypes.Length}";

			for(var i = 0; i < func.Locals.Length; ++i)
				Add($"{Rewrite(func.Locals[i])} l{i}");

			void Binary(string op) {
				var (b, a) = (Pop(), Pop());
				Push($"({a}) {op} ({b})");
			}

			void ToBool() => Push($"({Pop()}) ? 1 : 0");

			foreach(var _inst in func.Instructions) {
				switch(_inst) {
					case Instruction<uint> inst when inst == Opcode.get_global: Push(GlobalName(inst.Operand)); break;
					case Instruction<uint> inst when inst == Opcode.set_global: Add($"{GlobalName(inst.Operand)} = {Pop()}"); break;
					case Instruction<uint> inst when inst == Opcode.get_local: Push(LocalName(inst.Operand)); break;
					case Instruction<uint> inst when inst == Opcode.set_local: Add($"{LocalName(inst.Operand)} = {Pop()}"); break;
					case Instruction<int> inst when inst == Opcode.i32_const: Push(inst.Operand.ToString()); break;
					case Instruction<WasmType> inst when inst == Opcode.@if: StartBlock($"if(({Pop()}) != 0)"); break;
					case Instruction<uint> inst when inst == Opcode.call:
						var (fn, ft) = GetFunction(inst.Operand);
						var p = ft.ParamTypes.Select(_ => Pop()).ToList();
						var c = $"{fn}({string.Join(", ", p)})";
						if(ft.ReturnType == null)
							Add(c);
						else
							Push(c);
						break;
					default:
						switch(_inst.Op) {
							case Opcode.i32_add:
							case Opcode.i64_add:
							case Opcode.f32_add:
							case Opcode.f64_add:
								Binary("+");
								break;
							
							case Opcode.i32_and:
							case Opcode.i64_and:
								Binary("&");
								break;
							
							case Opcode.i32_ge_s:
								Binary(">=");
								ToBool();
								break;
							
							case Opcode.i32_eq:
							case Opcode.i64_eq:
							case Opcode.f32_eq:
							case Opcode.f64_eq:
								Binary("==");
								ToBool();
								break;
							
							case Opcode.end: EndBlock(); break;
							case Opcode.@return:
								Add(func.Signature.ReturnType == null ? "return" : $"return {Pop()}");
								break;
							
							default:
								throw new NotImplementedException($"Unhandled instruction: {_inst.ToPrettyString()}");
						}
						break;
				}
			}
			
			if(func.Signature.ReturnType != null && stack.Count != 0)
				Add($"return {Pop()}");
			
			EndBlock();

			return code;
		}

		(string, FuncType) GetFunction(uint num) {
			var fimp = Module.Imports.Where(x => x.Kind == ExternalKind.Function).ToList();
			if(num < fimp.Count) {
				var func = fimp[(int) num];
				return (func.FieldName, (FuncType) func.Type);
			}
			return ($"F{num - fimp.Count}", Module.FunctionSignatures[(int) num - fimp.Count]);
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
					var ts = ft.ReturnType == null ? "Action" : "Func";
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