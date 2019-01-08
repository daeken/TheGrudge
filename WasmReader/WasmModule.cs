namespace WasmReader {
	public class WasmModule {
		public FuncType[] FuncTypes;
		public WasmImport[] Imports;
		public FuncType[] FunctionSignatures;
		public WasmGlobal[] Globals;
		public WasmExport[] Exports;
		public WasmElement[] Elements;
		public WasmFunction[] Functions;
	}
}