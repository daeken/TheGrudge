namespace WasmReader {
	public class Instruction {
		public readonly Opcode Op;

		public Instruction(Opcode op) => Op = op;

		public override string ToString() => Op.ToString();
		
		public static implicit operator Opcode(Instruction inst) => inst.Op;
	}

	public class Instruction<T> : Instruction {
		public readonly T Operand;

		public Instruction(Opcode op, T operand) : base(op) => Operand = operand;

		public override string ToString() => $"{Op} {Operand}";

		public void Deconstruct(out Opcode op, out T operand) {
			op = Op;
			operand = Operand;
		}
	}
}