namespace WasmReader {
	public enum ExternalKind {
		Function, 
		Table, 
		Memory, 
		Global
	}
	
	public class WasmImport {
		public readonly string ModuleName, FieldName;
		public readonly ExternalKind Kind;
		public readonly WasmType Type;
		public readonly (uint Initial, uint Maximum)? ResizableLimits;

		public WasmImport(string moduleName, string fieldName, ExternalKind kind, WasmType type,
			(uint, uint)? resizableLimits = null) {
			ModuleName = moduleName;
			FieldName = fieldName;
			Kind = kind;
			Type = type;
			ResizableLimits = resizableLimits;
		}
	}
}