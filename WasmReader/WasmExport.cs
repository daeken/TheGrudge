namespace WasmReader {
	public class WasmExport {
		public readonly string FieldName;
		public readonly ExternalKind Kind;
		public readonly uint Index;

		public WasmExport(string fieldName, ExternalKind kind, uint index) {
			FieldName = fieldName;
			Kind = kind;
			Index = index;
		}
	}
}