SRC = $(wildcard *.c)

WASMO0 = $(SRC:.c=.O0.js)
WASMO1 = $(SRC:.c=.O1.js)

CCFLAGS = -c -emit-llvm -target i386-pc-none-eabi

%.O0.js: %.c
	emcc $< -O0 -s ALLOW_MEMORY_GROWTH=1 -s WASM=1 -o $@

%.O1.js: %.c
	emcc $< -O1 -s ALLOW_MEMORY_GROWTH=1 -s WASM=1 -o $@

all: $(WASMO0) $(WASMO1)

clean:
	rm *.wasm *.js
