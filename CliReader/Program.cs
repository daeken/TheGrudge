using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyModel;
using Transmutate;
using WasmReader;

namespace CliReader {
	class Program {
		static void Main(string[] args) {
			var module = Reader.Read(File.OpenRead(args[0]));
			var rw = new Rewriter(module, "TestNS", "TestModule");
			var code = rw.WriteCode();
			
			var coreDir = Directory.GetParent(typeof(object).Assembly.Location);
			var tree = SyntaxFactory.ParseSyntaxTree(code);
			var compilation = CSharpCompilation.Create(Path.GetFileName(args[1]).Split('.')[0])
				.WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true))
				.AddReferences(
					MetadataReference.CreateFromFile(typeof(WasmBootstrap.Module).Assembly.Location)
				)
				.AddReferences(
					DependencyContext.Default.CompileLibraries
						.SelectMany(cl => cl.ResolveReferencePaths())
						.Select(asm => MetadataReference.CreateFromFile(asm))
				)
				.AddSyntaxTrees(tree);
			var res = compilation.Emit(args[1]);
			if(!res.Success)
				foreach(var issue in res.Diagnostics)
					if(issue.Severity == DiagnosticSeverity.Error)
						Console.WriteLine($"ID: {issue.Id} Message: {issue.GetMessage()} Location: {issue.Location.GetLineSpan()} Severity: {issue.Severity}");
		}
	}
}