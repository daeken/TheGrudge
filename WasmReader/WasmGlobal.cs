using System.Collections.Generic;

namespace WasmReader {
	public class WasmGlobal {
		public readonly WasmType Type;
		public readonly List<Instruction> InitExpr;

		public WasmGlobal(WasmType type, List<Instruction> initExpr) {
			Type = type;
			InitExpr = initExpr;
		}
	}
}