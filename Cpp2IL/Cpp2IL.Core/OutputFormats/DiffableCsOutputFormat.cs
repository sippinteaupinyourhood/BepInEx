using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Cpp2IL.Core.Api;
using Cpp2IL.Core.Extensions;
using Cpp2IL.Core.Logging;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.Utils;
using LibCpp2IL;

namespace Cpp2IL.Core.OutputFormats;

public class DiffableCsOutputFormat : Cpp2IlOutputFormat
{
    public static bool IncludeMethodLength = false;

    public override string OutputFormatId => "diffable-cs";
    public override string OutputFormatName => "Diffable C#";

    public override void DoOutput(ApplicationAnalysisContext context, string outputRoot)
    {
        
        
        
        
        

        

        outputRoot = Path.Combine(outputRoot, "DiffableCs");

        if (Directory.Exists(outputRoot))
        {
            Logger.InfoNewline("Removing old DiffableCs output directory...", "DiffableCsOutputFormat");
            Directory.Delete(outputRoot, true);
        }

        Logger.InfoNewline("Building C# files and directory structure...", "DiffableCsOutputFormat");
        var files = BuildOutput(context, outputRoot);

        Logger.InfoNewline("Writing C# files...", "DiffableCsOutputFormat");
        foreach (var (filePath, fileContent) in files)
        {
            File.WriteAllText(filePath, fileContent.ToString());
        }
    }

    private static Dictionary<string, StringBuilder> BuildOutput(ApplicationAnalysisContext context, string outputRoot)
    {
        var ret = new Dictionary<string, StringBuilder>();

        foreach (var assembly in context.Assemblies)
        {
            var asmPath = Path.Combine(outputRoot, assembly.CleanAssemblyName);
            Directory.CreateDirectory(asmPath);

            foreach (var type in assembly.TopLevelTypes)
            {
                if (type is InjectedTypeAnalysisContext)
                    continue;

                var path = Path.Combine(asmPath, type.NamespaceAsSubdirs, MiscUtils.CleanPathElement(type.Name + ".cs"));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);

                var sb = new StringBuilder();

                
                if (!string.IsNullOrEmpty(type.Namespace))
                    sb.AppendLine($"namespace {type.Namespace};").AppendLine();
                else
                    sb.AppendLine("//Type is in global namespace").AppendLine();

                AppendType(sb, type);

                ret[path] = sb;
            }
        }

        return ret;
    }

    private static void AppendType(StringBuilder sb, TypeAnalysisContext type, int indent = 0)
    {
        
        
        

        
        AppendCustomAttributes(sb, type, indent);

        
        sb.Append('\t', indent);

        sb.Append(CsFileUtils.GetKeyWordsForType(type));
        sb.Append(' ');
        sb.Append(CsFileUtils.GetTypeName(type.Name));
        CsFileUtils.AppendInheritanceInfo(type, sb);
        sb.AppendLine();
        sb.Append('\t', indent);
        sb.Append('{');
        sb.AppendLine();

        
        indent++;

        if (type.IsEnumType)
        {
            var enumValues = type.Fields.Where(f => f.IsStatic).ToList();
            enumValues.SortByExtractedKey(e => e.Token); 
            foreach (var enumValue in enumValues)
            {
                sb.Append('\t', indent);
                sb.Append(enumValue.Name);
                sb.Append(" = ");
                sb.Append(enumValue.BackingData!.DefaultValue);
                sb.Append(',');
                sb.AppendLine();
            }
        }
        else
        {
            
            var nestedTypes = type.NestedTypes.Clone();
            nestedTypes.SortByExtractedKey(t => t.Name);
            foreach (var nested in nestedTypes)
                AppendType(sb, nested, indent);

            
            var fields = type.Fields.Clone();
            fields.SortByExtractedKey(f => f.IsStatic ? f.Offset : f.Offset + 0x1000);
            foreach (var field in fields)
                AppendField(sb, field, indent);

            sb.AppendLine();

            
            var events = type.Events.Clone();
            events.SortByExtractedKey(e => e.Name);
            foreach (var evt in events)
                AppendEvent(sb, evt, indent);

            
            var properties = type.Properties.Clone();
            properties.SortByExtractedKey(p => p.Name);
            foreach (var prop in properties)
                AppendProperty(sb, prop, indent);

            
            var methods = type.Methods.Clone();
            methods.SortByExtractedKey(m => m.Name);
            foreach (var method in methods)
                AppendMethod(sb, method, indent);
        }

        
        indent--;
        sb.Append('\t', indent);
        sb.Append('}');
        sb.AppendLine().AppendLine();
    }

    private static void AppendField(StringBuilder sb, FieldAnalysisContext field, int indent)
    {
        if (field is InjectedFieldAnalysisContext)
            return;

        
        AppendCustomAttributes(sb, field, indent);

        
        sb.Append('\t', indent);
        sb.Append(CsFileUtils.GetKeyWordsForField(field));
        sb.Append(' ');
        sb.Append(CsFileUtils.GetTypeName(field.FieldType.Name));
        sb.Append(' ');
        sb.Append(field.Name);

        if (field.BackingData?.DefaultValue is { } defaultValue)
        {
            sb.Append(" = ");

            if (defaultValue is string stringDefaultValue)
                sb.Append('"').Append(stringDefaultValue).Append('"');
            else if (defaultValue is char charDefaultValue)
                sb.Append("'\\u").Append(((int)charDefaultValue).ToString("X")).Append("'");
            else
                sb.Append(defaultValue);
        }

        sb.Append("; //Field offset: 0x");
        sb.Append(field.Offset.ToString("X"));

        if ((field.Attributes & FieldAttributes.HasFieldRVA) != 0 && field.BackingData != null)
        {
            sb.Append(" || Has Field RVA (address hidden for diffability)");
            
            
            

            var actualValue = field.BackingData.Field.StaticArrayInitialValue;
            if (actualValue is { Length: > 0 })
            {
                sb.Append(" || Field RVA Decoded (hex blob): [");
                sb.Append(actualValue[0].ToString("X2"));
                for (var i = 1; i < actualValue.Length; i++)
                {
                    var b = actualValue[i];
                    sb.Append(' ').Append(b.ToString("X2"));
                }

                sb.Append(']');
            }
        }

        sb.AppendLine();
    }

    private static void AppendEvent(StringBuilder sb, EventAnalysisContext evt, int indent)
    {
        
        AppendCustomAttributes(sb, evt, indent);

        
        sb.Append('\t', indent);
        sb.Append(CsFileUtils.GetKeyWordsForEvent(evt));
        sb.Append(' ');
        sb.Append(CsFileUtils.GetTypeName(evt.EventType.Name));
        sb.Append(' ');
        sb.Append(evt.Name).AppendLine();
        sb.Append('\t', indent);
        sb.Append('{');
        sb.AppendLine();

        
        indent++;
        if (evt.Adder != null)
            AppendAccessor(sb, evt.Adder, "add", indent);
        if (evt.Remover != null)
            AppendAccessor(sb, evt.Remover, "remove", indent);
        if (evt.Invoker != null)
            AppendAccessor(sb, evt.Invoker, "fire", indent);
        indent--;

        sb.Append('\t', indent);
        sb.Append('}');
        sb.AppendLine().AppendLine();
    }

    private static void AppendProperty(StringBuilder sb, PropertyAnalysisContext prop, int indent)
    {
        
        AppendCustomAttributes(sb, prop, indent);

        
        sb.Append('\t', indent);
        sb.Append(CsFileUtils.GetKeyWordsForProperty(prop));
        sb.Append(' ');
        sb.Append(CsFileUtils.GetTypeName(prop.PropertyType.Name));
        sb.Append(' ');
        sb.Append(prop.Name);
        sb.AppendLine();
        sb.Append('\t', indent);
        sb.Append('{');
        sb.AppendLine();

        
        indent++;
        if (prop.Getter != null)
            AppendAccessor(sb, prop.Getter, "get", indent);
        if (prop.Setter != null)
            AppendAccessor(sb, prop.Setter, "set", indent);
        indent--;

        sb.Append('\t', indent);
        sb.Append('}');
        sb.AppendLine().AppendLine();
    }

    private static void AppendMethod(StringBuilder sb, MethodAnalysisContext method, int indent)
    {
        if (method is InjectedMethodAnalysisContext)
            return;

        
        AppendCustomAttributes(sb, method, indent);

        
        sb.Append('\t', indent);
        sb.Append(CsFileUtils.GetKeyWordsForMethod(method));
        sb.Append(' ');
        if (method.Name is not ".ctor" and not ".cctor")
        {
            sb.Append(CsFileUtils.GetTypeName(method.ReturnType.Name));
            sb.Append(' ');
            sb.Append(method.Name);
        }
        else
        {
            
            sb.Append(method.DeclaringType!.Name);
        }

        sb.Append('(');
        sb.Append(CsFileUtils.GetMethodParameterString(method));
        sb.Append(") { }");

        if (IncludeMethodLength)
        {
            sb.Append(" //Length: ");
            sb.Append(method.RawBytes.Length);
        }

        sb.AppendLine().AppendLine();
    }

    
    private static void AppendAccessor(StringBuilder sb, MethodAnalysisContext accessor, string accessorType, int indent)
    {
        
        AppendCustomAttributes(sb, accessor, indent);

        sb.Append('\t', indent);
        sb.Append(CsFileUtils.GetKeyWordsForMethod(accessor, true, true));
        sb.Append(' ');
        sb.Append(accessorType);
        sb.Append(" { } //Length: ");
        sb.Append(accessor.RawBytes.Length);
        sb.AppendLine();
    }

    private static void AppendCustomAttributes(StringBuilder sb, HasCustomAttributes owner, int indent)
        => sb.Append(CsFileUtils.GetCustomAttributeStrings(owner, indent, true, true));
}
