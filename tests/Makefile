SRC = $(wildcard *.c)

WASMO0 = $(SRC:.c=.O0.wasm)
WASMO1 = $(SRC:.c=.O1.wasm)

CCFLAGS = -c -emit-llvm -target i386-pc-none-eabi

%.O0.wasm: %.c
	emcc $< -O0 -s SIDE_MODULE=1 -s WASM=1 -o $@

%.O1.wasm: %.c
	emcc $< -O1 -s SIDE_MODULE=1 -s WASM=1 -o $@

all: $(WASMO0) $(WASMO1)

clean:
	rm *.wasm
