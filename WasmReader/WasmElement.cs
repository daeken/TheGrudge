using System.Collections.Generic;

namespace WasmReader {
	public class WasmElement {
		public readonly uint Index;
		public readonly List<Instruction> Offset;
		public readonly uint[] Elems;

		public WasmElement(uint index, List<Instruction> offset, uint[] elems) {
			Index = index;
			Offset = offset;
			Elems = elems;
		}
	}
}