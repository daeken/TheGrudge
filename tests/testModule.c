#include <emscripten.h>
#include <stdlib.h>

/*EMSCRIPTEN_KEEPALIVE int addTest(int a, int b) {
	return a + b;
}*/

EMSCRIPTEN_KEEPALIVE int addPointerTest(int a, int b) {
	volatile int* temp = (int*) malloc(sizeof(int));
	*temp = a;
	*temp += b;
	return *temp;
}
