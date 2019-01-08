#include <emscripten.h>

EMSCRIPTEN_KEEPALIVE int addTest(int a, int b) {
	return a + b;
}