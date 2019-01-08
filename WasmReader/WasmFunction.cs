using System.Collections.Generic;

namespace WasmReader {
	public class WasmFunction {
		public readonly FuncType Signature;
		public readonly WasmType[] Locals;
		public readonly List<Instruction> Instructions;

		public WasmFunction(FuncType signature, WasmType[] locals, List<Instruction> instructions) {
			Signature = signature;
			Locals = locals;
			Instructions = instructions;
		}
	}
}