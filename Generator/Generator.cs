using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Generator
{
    public class Generator
    {
        public class ClassInfo
        {
            public string ClassName { get; }
            public string TestsFile { get; }

            public ClassInfo(string className, string testsFile)
            {
                ClassName = className;
                TestsFile = testsFile;
            }
        }

        private class ClassData
        {
            public string ClassName { get; }

            public MemberDeclarationSyntax TestClassDeclarationSyntax { get; }

            public ClassData(string className, MemberDeclarationSyntax testClassDeclarationSyntax)
            {
                ClassName = className;
                TestClassDeclarationSyntax = testClassDeclarationSyntax;
            }

        }

        public List<ClassInfo> Generate(string source)
        {
            var root = CSharpSyntaxTree.ParseText(source).GetCompilationUnitRoot();

            var usings = root.DescendantNodes().OfType<UsingDirectiveSyntax>();
            var classes = root.DescendantNodes().OfType<ClassDeclarationSyntax>()
                .Where(node => node.Modifiers.Any(n => n.Kind() == SyntaxKind.PublicKeyword)).ToList();

            var classesDeclarations = classes.Select(GenerateTestsClassWithParents).ToList();

            return classesDeclarations.Select(classData => new ClassInfo(classData.ClassName,
                CompilationUnit()
                    .WithUsings(new SyntaxList<UsingDirectiveSyntax>(usings)
                    .Add(UsingDirective(QualifiedName(IdentifierName("NUnit"), IdentifierName("Framework")))))
                    .AddMembers(classData.TestClassDeclarationSyntax)
                    .NormalizeWhitespace().ToFullString())).ToList();
        }

        private ClassData GenerateTestsClassWithParents(ClassDeclarationSyntax classDeclaration)
        {
            SyntaxNode? current = classDeclaration;
            var testClassDeclaration = GenerateTestsClass(classDeclaration);
            var isFirstNamespace = true;
            var currentTree = current;
            var stringBuilder = new StringBuilder(testClassDeclaration.Identifier.Text);
            while (current.Parent is NamespaceDeclarationSyntax parent)
            {
                NamespaceDeclarationSyntax ns;
                if (isFirstNamespace)
                {
                    ns = NamespaceDeclaration(IdentifierName(parent.Name + ".Tests"))
                        .WithMembers(new SyntaxList<MemberDeclarationSyntax>(testClassDeclaration));
                    isFirstNamespace = false;
                }
                else
                {
                    ns = NamespaceDeclaration(parent.Name)
                        .WithMembers(new SyntaxList<MemberDeclarationSyntax>((NamespaceDeclarationSyntax)currentTree));
                }

                currentTree = ns;
                stringBuilder.Insert(0, $"{ns.Name}.");
                current = current.Parent;
            }

            return new ClassData(stringBuilder.ToString(), (MemberDeclarationSyntax)currentTree);
        }

        private ClassDeclarationSyntax GenerateTestsClass(ClassDeclarationSyntax classDeclaration)
        {
            var methods = classDeclaration.DescendantNodes().OfType<MethodDeclarationSyntax>()
                .Where(node => node.Modifiers.Any(n => n.Kind() == SyntaxKind.PublicKeyword)).ToList();
            methods.Sort((method1, method2) => string.Compare(method1.Identifier.Text, method2.Identifier.Text, StringComparison.Ordinal));

            var testMethods = new MemberDeclarationSyntax[methods.Count];
            var methodIndex = 0;
            for (var i = 0; i < methods.Count; ++i)
            {
                //arrange section
                var methodBody = new List<StatementSyntax>();
                var methodArgs = new List<SyntaxNodeOrToken>();
                foreach (var parameter in methods[i].ParameterList.Parameters)
                {
                    methodBody.Add(LocalDeclarationStatement(
                        VariableDeclaration(parameter.Type!).WithVariables(
                            SingletonSeparatedList(VariableDeclarator(Identifier(parameter.Identifier.Text))
                                .WithInitializer(EqualsValueClause(LiteralExpression(
                                    SyntaxKind.DefaultLiteralExpression, Token(SyntaxKind.DefaultKeyword))))))));
                    methodArgs.Add(Argument(IdentifierName(parameter.Identifier.Text)));
                    methodArgs.Add(Token(SyntaxKind.CommaToken));
                }

                //remove comma
                if (methodArgs.Count != 0)
                {
                    methodArgs.RemoveAt(methodArgs.Count - 1);
                }

                var callerName = methods[i].Modifiers.Any(n => n.Kind() == SyntaxKind.StaticKeyword)
                    ? classDeclaration.Identifier.Text
                    : $"_{classDeclaration.Identifier.Text.ToCamelCase()}";

                if (methods[i].ReturnType is PredefinedTypeSyntax predefinedTypeSyntax
                    && predefinedTypeSyntax.Keyword.ValueText == Token(SyntaxKind.VoidKeyword).ValueText)
                {
                    //act section
                    methodBody.Add(ExpressionStatement(
                        InvocationExpression(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                                IdentifierName(callerName), IdentifierName(methods[i].Identifier.Text)))
                            .WithArgumentList(ArgumentList(SeparatedList<ArgumentSyntax>(methodArgs)))));
                }
                else
                {
                    //act section
                    methodBody.Add(LocalDeclarationStatement(
                        VariableDeclaration(IdentifierName(Identifier(TriviaList(), SyntaxKind.VarKeyword, "var",
                            "var", TriviaList()))).WithVariables(SingletonSeparatedList(
                            VariableDeclarator(Identifier("actual")).WithInitializer(EqualsValueClause(
                                InvocationExpression(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                                        IdentifierName(callerName),
                                        IdentifierName(methods[i].Identifier.Text)))
                                    .WithArgumentList(
                                        ArgumentList(SeparatedList<ArgumentSyntax>(methodArgs)))))))));

                    //assert section
                    methodBody.Add(LocalDeclarationStatement(
                        VariableDeclaration(methods[i].ReturnType).WithVariables(
                            SingletonSeparatedList(VariableDeclarator(Identifier("expected"))
                                .WithInitializer(EqualsValueClause(LiteralExpression(
                                    SyntaxKind.DefaultLiteralExpression, Token(SyntaxKind.DefaultKeyword))))))));

                    //assert.That(actual, Is.EqualTo(expected));
                    methodBody.Add(ExpressionStatement(
                        InvocationExpression(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                            IdentifierName("Assert"), IdentifierName("That"))).WithArgumentList(ArgumentList(
                            SeparatedList<ArgumentSyntax>(new SyntaxNodeOrToken[]
                            {
                                Argument(IdentifierName("actual")), Token(SyntaxKind.CommaToken),
                                Argument(InvocationExpression(MemberAccessExpression(
                                        SyntaxKind.SimpleMemberAccessExpression, IdentifierName("Is"),
                                        IdentifierName("EqualTo")))
                                    .WithArgumentList(
                                        ArgumentList(SingletonSeparatedList(Argument(IdentifierName("expected"))))))
                            })))));
                }

                //assert.Fail("autogenerated");
                methodBody.Add(ExpressionStatement(
                    InvocationExpression(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                            IdentifierName("Assert"), IdentifierName("Fail")))
                        .WithArgumentList(ArgumentList(SingletonSeparatedList(
                            Argument(LiteralExpression(SyntaxKind.StringLiteralExpression,
                                Literal("autogenerated"))))))));

                //get method name
                if (i != 0 && methods[i].Identifier.Text == methods[i - 1].Identifier.Text)
                {
                    methodIndex++;
                }
                else if (i != methods.Count - 1 && methods[i].Identifier.Text == methods[i + 1].Identifier.Text)
                {
                    methodIndex = 0;
                }
                else
                {
                    methodIndex = -1;
                }
                var methodName = methods[i].Identifier.Text + (methodIndex != -1 ? $"{methodIndex}" : "") + "Test";

                var method = MethodDeclaration(PredefinedType(Token(SyntaxKind.VoidKeyword)), Identifier(methodName))
                    .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)))
                    .WithAttributeLists(
                        SingletonList(AttributeList(SingletonSeparatedList(Attribute(IdentifierName("Test"))))))
                    .WithBody(Block(methodBody));

                testMethods[i] = method;
            }

            var classDecl = ClassDeclaration(classDeclaration.Identifier.Text + "Tests")
                .AddModifiers(Token(SyntaxKind.PublicKeyword))
                .WithMembers(new SyntaxList<MemberDeclarationSyntax>(testMethods))
                .WithAttributeLists(
                    SingletonList(AttributeList(SingletonSeparatedList(Attribute(IdentifierName("TestFixture"))))));
            return classDecl;
        }
    }
}
