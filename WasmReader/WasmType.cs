using System;

namespace WasmReader {
	public abstract class WasmType {
		public bool Mutable = true;
	}

	public class FuncType : WasmType {
		public readonly WasmType[] ParamTypes;
		public readonly WasmType ReturnType;

		public FuncType(WasmType[] paramTypes, WasmType returnType) {
			ParamTypes = paramTypes;
			ReturnType = returnType;
		}
	}

	public class PrimitiveType : WasmType {
		public readonly Type IlType;

		public PrimitiveType(Type ilType) => IlType = ilType;
	}
	
	public class EmptyBlockType : WasmType {}
}