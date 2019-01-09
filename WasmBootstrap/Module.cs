using System;
using System.Runtime.InteropServices;

namespace WasmBootstrap {
	public static unsafe partial class Module {
		public static int __memory_base;
		public static int MemorySize = 1 * 1024 * 1024;
		public static byte* ActualMemoryBase = (byte*) Marshal.AllocHGlobal(MemorySize);

		public static int DYNAMICTOP_PTR = MemorySize - 4;
		public static int STACK_MAX = MemorySize;
		public static int STACKTOP = STACK_MAX - 65536;

		public static int tempDoublePtr = 0;

		public static int __errno;
		public static void ___setErrNo(int errno) => __errno = errno;

		public static void ___assert_fail(int condition, int filename, int line, int func) =>
			throw new Exception($"___assert_fail({condition}, {filename}, {line}, {func})");
		
		public static void abort(int v) => throw new Exception($"Abort {v}");
		public static void _abort() => throw new Exception("Abort");

		public static int growMemory(int pages) {
			var old = MemorySize / 65536;
			MemorySize = pages * 65536;
			ActualMemoryBase = (byte*) Marshal.ReAllocHGlobal((IntPtr) ActualMemoryBase, (IntPtr) MemorySize);
			return old;
		}
		public static int getTotalMemory() => MemorySize;

		public static int enlargeMemory() => throw new NotImplementedException();

		public static int _emscripten_memcpy_big(int dest, int src, int num) {
			Buffer.MemoryCopy(src - __memory_base + ActualMemoryBase, dest - __memory_base + ActualMemoryBase, num, num);
			return dest;
		}

		static void Checked(int addr, int size, Action cb) {
			if(addr >= __memory_base && addr + size <= __memory_base + MemorySize)
				cb();
			else
				throw new Exception($"Write out of range: {addr} {size} -- {__memory_base}-{__memory_base+MemorySize}");
		}
		
		static T Checked<T>(int addr, int size, Func<T> cb) {
			if(addr >= __memory_base && addr + size <= __memory_base + MemorySize)
				return cb();
			throw new Exception($"Load out of range: {addr} {size} -- {__memory_base}-{__memory_base+MemorySize}");
		}
		
		public static void __Storei32(int addr, int value) => Checked(addr, 4, () => *(int*) (addr - __memory_base + ActualMemoryBase) = value);
		public static int __Loadi32(int addr) => Checked(addr, 4, () => *(int*) (addr - __memory_base + ActualMemoryBase));
		public static void __Storei32_8(int addr, int value) => Checked(addr, 1, () => *(addr - __memory_base + ActualMemoryBase) = unchecked((byte) (uint) value));
		public static int __Loadi32_8s(int addr) => Checked(addr, 1, () => *(sbyte*) (addr - __memory_base + ActualMemoryBase));
		public static int __Loadi32_8u(int addr) => Checked(addr, 1, () => *(addr - __memory_base + ActualMemoryBase));
		public static void __Storei32_16(int addr, int value) => Checked(addr, 2, () => *(ushort*)(addr - __memory_base + ActualMemoryBase) = unchecked((ushort) (uint) value));
		public static int __Loadi32_16s(int addr) => Checked(addr, 2, () => *(short*) (addr - __memory_base + ActualMemoryBase));
		public static int __Loadi32_16u(int addr) => Checked(addr, 2, () => *(ushort*) (addr - __memory_base + ActualMemoryBase));
		public static void __Storei64(int addr, long value) => Checked(addr, 8, () => *(long*) (addr - __memory_base + ActualMemoryBase) = value);
		public static long __Loadi64(int addr) => Checked(addr, 8, () => *(long*) (addr - __memory_base + ActualMemoryBase));
		public static void __Storei64_8(int addr, long value) => Checked(addr, 1, () => *(addr - __memory_base + ActualMemoryBase) = unchecked((byte) (uint) value));
		public static long __Loadi64_8s(int addr) => Checked(addr, 1, () => *(sbyte*) (addr - __memory_base + ActualMemoryBase));
		public static long __Loadi64_8u(int addr) => Checked(addr, 1, () => *(addr - __memory_base + ActualMemoryBase));
		public static void __Storei64_16(int addr, long value) => Checked(addr, 2, () => *(ushort*)(addr - __memory_base + ActualMemoryBase) = unchecked((ushort) (uint) value));
		public static long __Loadi64_16s(int addr) => Checked(addr, 2, () => *(short*) (addr - __memory_base + ActualMemoryBase));
		public static long __Loadi64_16u(int addr) => Checked(addr, 2, () => *(ushort*) (addr - __memory_base + ActualMemoryBase));
		public static void __Storei64_32(int addr, long value) => Checked(addr, 4, () => *(ushort*)(addr - __memory_base + ActualMemoryBase) = unchecked((ushort) (uint) value));
		public static long __Loadi64_32s(int addr) => Checked(addr, 4, () => *(int*) (addr - __memory_base + ActualMemoryBase));
		public static long __Loadi64_32u(int addr) => Checked(addr, 4, () => *(uint*) (addr - __memory_base + ActualMemoryBase));
		public static void __Storef32(int addr, float value) => Checked(addr, 4, () => *(float*) (addr - __memory_base + ActualMemoryBase) = value);
		public static float __Loadf32(int addr) => Checked(addr, 4, () => *(float*) (addr - __memory_base + ActualMemoryBase));
		public static void __Storef64(int addr, double value) => Checked(addr, 8, () => *(double*) (addr - __memory_base + ActualMemoryBase) = value);
		public static double __Loadf64(int addr) => Checked(addr, 8, () => *(double*) (addr - __memory_base + ActualMemoryBase));

		public static int Reinterpret_i32(float value) => *(int*) &value;
		public static float Reinterpret_f32(int value) => *(float*) &value;
		public static float Reinterpret_f32(uint value) => *(float*) &value;
		public static long Reinterpret_i64(float value) => *(long*) &value;
		public static double Reinterpret_f64(long value) => *(double*) &value;
		public static double Reinterpret_f64(ulong value) => *(double*) &value;

		public static double f64_rem(double v, double m) => v % m;

		public static object[] table;
		
		public static void abortStackOverflow(int size) => throw new Exception($"abortStackOverflow({size})");
		
		public static void nullFunc_ii(int a) => throw new Exception($"Invalid function pointer called");
		public static void nullFunc_iiii(int a) => throw new Exception($"Invalid function pointer called");

		public static double NaN = double.NaN;
		public static double Infinity = double.PositiveInfinity;
	}
}