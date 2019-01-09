using System;

namespace WasmBootstrap {
	public static unsafe partial class Module {
		public static int _pthread_once(int once_control, int init_routine) => throw new NotImplementedException();
		public static int _pthread_getspecific(int key) => throw new NotImplementedException();
		public static int _pthread_setspecific(int key, int value) => throw new NotImplementedException();
		public static int _pthread_key_create(int key, int destructor) => throw new NotImplementedException();
	}
}