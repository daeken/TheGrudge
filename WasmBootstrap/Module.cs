using System;
using System.Runtime.InteropServices;

namespace WasmBootstrap {
	public unsafe static class Module {
		public static int __memory_base;
		public static int CurMemoryBase;
		public static byte* ActualMemoryBase = (byte*) Marshal.AllocHGlobal(65536);

		public static int _malloc(int size) {
			var addr = CurMemoryBase;
			CurMemoryBase += size;
			return addr;
		}

		public static void __Store(int addr, int value) => *(int*) (addr - __memory_base + ActualMemoryBase) = value;
		public static int __Load(int addr) => *(int*) (addr - __memory_base + ActualMemoryBase);
	}
}