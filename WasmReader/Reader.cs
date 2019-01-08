using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using PrettyPrinter;

namespace WasmReader {
	public class Reader {
		readonly BinaryReader Br;
		readonly WasmModule Module = new WasmModule();
		
		public static WasmModule Read(Stream stream) => new Reader(stream).Module;
		
		public Reader(Stream stream) {
			Br = new BinaryReader(stream);
			
			if(Br.ReadUInt32() != 0x6d736100) throw new NotSupportedException("Bad magic");
			if(Br.ReadUInt32() != 1) throw new NotSupportedException("Non-1 version");
			
			var streamLength = stream.Length;
			while(stream.Position < streamLength) {
				var id = VarU7();
				var payloadLen = (int) VarU32();
				var end = (int) stream.Position + payloadLen;
				string name = null;
				if(id == 0) {
					var nameLen = VarU32();
					var nameBytes = Br.ReadBytes((int) nameLen);
					name = Encoding.UTF8.GetString(nameBytes);
				}

				switch(id) {
					case 1:
						Module.FuncTypes = Enumerable.Range(0, (int) VarU32()).Select(_ => (FuncType) ParseType())
							.ToArray();
						break;
					
					case 2:
						Module.Imports = Enumerable.Range(0, (int) VarU32()).Select(_ => {
							var moduleName = Encoding.UTF8.GetString(Br.ReadBytes((int) VarU32()));
							var fieldName = Encoding.UTF8.GetString(Br.ReadBytes((int) VarU32()));
							switch(Br.ReadByte()) {
								case 0: // Function
									return new WasmImport(moduleName, fieldName, ExternalKind.Function, Module.FuncTypes[(int) VarU32()]);
								case 1: { // Table
									var tableType = ParseType();
									var hasMax = VarU1();
									var limits = (Initial: VarU32(), Maximum: hasMax ? VarU32() : 0xFFFFFFFFU);
									return new WasmImport(moduleName, fieldName, ExternalKind.Table, tableType, limits);
								}
								case 2: { // Memory
									var hasMax = VarU1();
									var limits = (Initial: VarU32(), Maximum: hasMax ? VarU32() : 0xFFFFFFFFU);
									return new WasmImport(moduleName, fieldName, ExternalKind.Memory, null, limits);
								}
								case 3: // Global
									var globalType = ParseType();
									globalType.Mutable = VarU1();
									return new WasmImport(moduleName, fieldName, ExternalKind.Global, globalType);
								default: throw new NotSupportedException();
							}
						}).ToArray();
						break;
					
					case 3:
						Module.FunctionSignatures = Enumerable.Range(0, (int) VarU32()).Select(_ => Module.FuncTypes[(int) VarU32()]).ToArray();
						break;
					
					case 6:
						Module.Globals = Enumerable.Range(0, (int) VarU32()).Select(_ => {
							var globalType = ParseType();
							globalType.Mutable = VarU1();
							var initExpr = ParseInstructions();
							return new WasmGlobal(globalType, initExpr);
						}).ToArray();
						break;
					
					case 7:
						Module.Exports = Enumerable.Range(0, (int) VarU32()).Select(_ => {
							var fieldName = Encoding.UTF8.GetString(Br.ReadBytes((int) VarU32()));
							var kind = (ExternalKind) Br.ReadByte();
							return new WasmExport(fieldName, kind, VarU32());
						}).ToArray();
						break;
					
					case 9:
						Module.Elements = Enumerable.Range(0, (int) VarU32()).Select(_ => {
							var index = VarU32();
							var initExpr = ParseInstructions();
							var elems = Enumerable.Range(0, (int) VarU32()).Select(__ => VarU32()).ToArray();
							return new WasmElement(index, initExpr, elems);
						}).ToArray();
						break;
					
					case 10:
						Module.Functions = Enumerable.Range(0, (int) VarU32()).Select(i => {
							var bodySize = VarU32();
							var bend = stream.Position + bodySize;
							var localTypes = Enumerable.Range(0, (int) VarU32()).Select(_ => {
								var count = VarU32();
								var type = ParseType();
								return Enumerable.Range(0, (int) count).Select(__ => type);
							}).SelectMany(x => x).ToArray();
							var insts = ParseInstructions();
							Debug.Assert(stream.Position == bend);
							return new WasmFunction(Module.FunctionSignatures[i], localTypes, insts);
						}).ToArray();
						break;

					case 0: // Custom section
					default:
						var payload = Br.ReadBytes(end - (int) stream.Position);
						break;
				}
				Debug.Assert(end == stream.Position);
			}
		}

		WasmType ParseType() {
			switch(VarU7()) {
				case 0x60: return ParseFuncType();
				case 0x7f: return new PrimitiveType(typeof(int));
				case 0x7e: return new PrimitiveType(typeof(long));
				case 0x7d: return new PrimitiveType(typeof(float));
				case 0x7c: return new PrimitiveType(typeof(double));
				case 0x70: return new PrimitiveType(typeof(FuncType));
				case 0x40: return new EmptyBlockType();
				case byte x: throw new NotSupportedException($"Unknown type constructor: 0x{x:X02}");
			}
		}

		FuncType ParseFuncType() =>
			new FuncType(
				Enumerable.Range(0, (int) VarU32()).Select(_ => ParseType()).ToArray(), 
				VarU1() ? ParseType() : null
			);

		List<Instruction> ParseInstructions() {
			var depth = 0;
			var insts = new List<Instruction>();
			while(true) {
				var op = (Opcode) Br.ReadByte();
				void Add<T>(T value) => insts.Add(new Instruction<T>(op, value));
				switch(op) {
					case Opcode.end:
						if(depth-- == 0)
							return insts;
						insts.Add(new Instruction(op));
						break;
					
					case Opcode.i32_const: Add(VarI32()); break;
					case Opcode.i64_const: Add(VarI64()); break;
					case Opcode.f32_const: Add(Br.ReadSingle()); break;
					case Opcode.f64_const: Add(Br.ReadDouble()); break;
					
					case Opcode.get_local: Add(VarU32()); break;
					case Opcode.set_local: Add(VarU32()); break;
					case Opcode.tee_local: Add(VarU32()); break;
					case Opcode.get_global: Add(VarU32()); break;
					case Opcode.set_global: Add(VarU32()); break;
					
					case Opcode.block: Add(ParseType()); depth++; break;
					case Opcode.loop: Add(ParseType()); depth++; break;
					case Opcode.@if: Add(ParseType()); depth++; break;
					case Opcode.br: Add(VarU32()); break;
					case Opcode.br_if: Add(VarU32()); break;
					case Opcode.br_table: throw new NotImplementedException();
					
					case Opcode.call: Add(VarU32()); break;
					case Opcode.call_indirect:
						Add(VarU32());
						VarU1();
						break;

					case Opcode.i32_load: throw new NotImplementedException();
					case Opcode.i64_load: throw new NotImplementedException();
					case Opcode.f32_load: throw new NotImplementedException();
					case Opcode.f64_load: throw new NotImplementedException();
					case Opcode.i32_load8_s: throw new NotImplementedException();
					case Opcode.i32_load8_u: throw new NotImplementedException();
					case Opcode.i32_load16_s: throw new NotImplementedException();
					case Opcode.i32_load16_u: throw new NotImplementedException();
					case Opcode.i64_load8_s: throw new NotImplementedException();
					case Opcode.i64_load8_u: throw new NotImplementedException();
					case Opcode.i64_load16_s: throw new NotImplementedException();
					case Opcode.i64_load16_u: throw new NotImplementedException();
					case Opcode.i64_load32_s: throw new NotImplementedException();
					case Opcode.i64_load32_u: throw new NotImplementedException();
					case Opcode.i32_store: throw new NotImplementedException();
					case Opcode.i64_store: throw new NotImplementedException();
					case Opcode.f32_store: throw new NotImplementedException();
					case Opcode.f64_store: throw new NotImplementedException();
					case Opcode.i32_store8: throw new NotImplementedException();
					case Opcode.i32_store16: throw new NotImplementedException();
					case Opcode.i64_store8: throw new NotImplementedException();
					case Opcode.i64_store16: throw new NotImplementedException();
					case Opcode.i64_store32: throw new NotImplementedException();
					case Opcode.current_memory: throw new NotImplementedException();
					case Opcode.grow_memory: throw new NotImplementedException();

					case Opcode x: insts.Add(new Instruction(x)); break;
				}
			}
		}

		bool VarU1() => Br.ReadByte() != 0;
		byte VarU7() {
			var v = Br.ReadByte();
			if((v & 0x80) != 0) throw new NotSupportedException("varuint7 has high bit set");
			return v;
		}
		uint VarU32() {
			var v = 0U;
			for(var i = 0; i < 5; ++i) {
				var b = Br.ReadByte();
				v |= ((uint) b & 0x7F) << (i * 7);
				if((b & 0x80) == 0) break;
				if(i == 4) throw new NotSupportedException("varuint32 has high bit set in 5th byte");
			}
			return v;
		}

		int VarI32() {
			var v = 0U;
			var shift = 0;
			byte b = 0;
			for(var i = 0; i < 5; ++i) {
				b = Br.ReadByte();
				v |= ((uint) b & 0x7F) << shift;
				shift += 7;
				if((b & 0x80) == 0) break;
				if(i == 4) throw new NotSupportedException("varuint32 has high bit set in 5th byte");
			}
			if(shift < 32 && (b & 0x40) != 0)
				v |= ~0U << shift;
			return (int) v;
		}

		long VarI64() {
			var v = 0UL;
			var shift = 0;
			byte b = 0;
			for(var i = 0; i < 10; ++i) {
				b = Br.ReadByte();
				v |= ((uint) b & 0x7F) << shift;
				shift += 7;
				if((b & 0x80) == 0) break;
				if(i == 9) throw new NotSupportedException("varint64 has high bit set in 10th byte");
			}
			if(shift < 64 && (b & 0x40) != 0)
				v |= ~0U << shift;
			return (long) v;
		}
	}
}