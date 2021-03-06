using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System;
using System.Text;
using UnityEditor;
using System.IO;

namespace ReactUnity.Editor.Developer
{
    public static class TypescriptModelsGenerator
    {
#if REACT_UNITY_DEVELOPER
        [MenuItem("React/Developer/Generate Unity Typescript Models", priority = 0)]
        public static void GenerateUnity()
        {
            Generate(
                new List<Assembly> {
                    typeof(UnityEngine.GameObject).Assembly,
                    typeof(UnityEngine.Video.VideoPlayer).Assembly,
                    typeof(UnityEngine.AudioSource).Assembly,
                    typeof(UnityEngine.CanvasGroup).Assembly,
                    typeof(UnityEngine.UI.Selectable).Assembly,
                    typeof(UnityEngine.UIVertex).Assembly,
                    typeof(UnityEngine.Input).Assembly,
                    typeof(UnityEngine.Animator).Assembly,
                    typeof(UnityEngine.Event).Assembly,
//#if REACT_INPUT_SYSTEM
//                    typeof(UnityEngine.InputSystem.InputSystem).Assembly,
//                    typeof(UnityEngine.InputSystem.UI.ExtendedPointerEventData).Assembly,
//#endif
//#if REACT_VECTOR_GRAPHICS
//                    typeof(Unity.VectorGraphics.VectorUtils).Assembly,
//#endif
                },
                new List<string> { "Unity", "UnityEngine" },
                new List<string> { },
                new Dictionary<string, string>()
            );
        }

        [MenuItem("React/Developer/Generate React Unity Typescript Models", priority = 0)]
        public static void GenerateReactUnity()
        {
            Generate(
                new List<Assembly> { typeof(ReactUnity).Assembly },
                new List<string> { "ReactUnity" },
                new List<string> { "UnityEngine.InputSystem" },
                new Dictionary<string, string> { { "UnityEngine", "unity" }, { "Unity", "unity" } }
            );
        }
#endif

        static List<string> IncludedNamespaces;
        static List<string> ExcludedNamespaces;
        static Dictionary<string, string> ImportNamespaces;
        static HashSet<string> Imports;

        static void Generate(List<Assembly> assemblies, List<string> include, List<string> exclude, Dictionary<string, string> import)
        {
            var filePath = EditorUtility.OpenFilePanel("Typescript file", "", "ts");
            if (string.IsNullOrWhiteSpace(filePath)) return;

            ImportNamespaces = import ?? new Dictionary<string, string>();
            IncludedNamespaces = include ?? new List<string>();
            ExcludedNamespaces = exclude ?? new List<string>();
            Imports = new HashSet<string>();

            var res = GetTypescript(assemblies);
            File.WriteAllText(filePath, res);

            UnityEngine.Debug.Log("Saved typescript models to: " + filePath);
        }

        static string GetTypescript(List<Assembly> assemblies)
        {
            var types = assemblies.Distinct().SelectMany(a => a.GetTypes()).Where(filterType).OrderBy(x => x.Namespace).Append(null);
            var sb = new StringBuilder();

            var nsStack = new Stack<string>();
            var n = "\n";

            string spaces(int depth = 0)
            {
                if ((nsStack.Count + depth) == 0) return "";
                return new String(' ', (nsStack.Count + depth) * 2);
            }

            foreach (var type in types)
            {
                var lastNs = nsStack.Count > 0 ? nsStack.Peek() : "";
                var ns = type?.Namespace ?? "";
                while (lastNs != ns)
                {
                    if (nsStack.Count == 0 || ns.Contains(lastNs + "."))
                    {
                        // Go deeper
                        var nsName = string.IsNullOrWhiteSpace(lastNs) ? ns : ns.Replace(lastNs + ".", "");
                        var splits = nsName.Split('.');

                        var curName = lastNs;
                        foreach (var split in splits)
                        {
                            curName = string.IsNullOrWhiteSpace(curName) ? split : $"{curName}.{split}";
                            sb.Append($"{spaces()}export namespace {split} {{{n}");
                            nsStack.Push(curName);
                        }
                        lastNs = ns;
                    }
                    else
                    {
                        nsStack.Pop();
                        lastNs = nsStack.Count > 0 ? nsStack.Peek() : "";
                        sb.Append($"{spaces()}}}{n}");
                    }
                }

                if (type == null) break;

                var bl = spaces();
                var bl1 = spaces(1);

                if (type.IsEnum)
                {
                    sb.Append($"{bl}export enum {getTypesScriptType(type, false)} {{{n}");
                    var fields = type.GetFields().Where(x => x.Name != "value__");

                    foreach (var info in fields)
                        sb.Append($"{bl1}{info.Name} = {getTypeScriptValue(info.GetRawConstantValue())},{n}");
                }
                else
                {
                    sb.Append($"{bl}export interface {getTypesScriptType(type, false)} {{{n}");

                    var props = type.GetProperties(BindingFlags.Instance | BindingFlags.Public).Where(x => !x.IsSpecialName && x.GetIndexParameters().Length == 0);
                    var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public).Where(x => !x.IsSpecialName);
                    var methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                        .Where(x => !x.IsSpecialName &&
                                    !x.GetParameters().Any(p => p.ParameterType.IsByRef || p.ParameterType.IsPointer) &&
                                    !x.IsGenericMethod)
                        .GroupBy(x => x.Name);

                    foreach (var info in props)
                        sb.Append($"{bl1}{getTypeScriptString(info)}{n}");

                    foreach (var info in fields)
                        sb.Append($"{bl1}{getTypeScriptString(info)}{n}");

                    foreach (var info in methods)
                        sb.Append($"{bl1}{getTypeScriptString(info)}{n}");
                }

                sb.Append($"{bl}}}{n}");
            }

            var importGroups = Imports.GroupBy(x => ImportNamespaces[x]);

            return $"//{n}" +
                $"// Types in assemblies: {string.Join(", ", assemblies.Select(x => x.GetName().Name))}{n}" +
                $"// Generated {DateTime.Now}{n}" +
                $"//{n}" +
                $"{string.Join(n, importGroups.Select(x => $"import {{ {string.Join(",", x)} }} from './{x.Key}';"))}{n}" +
                n +
                sb;
        }

        static bool filterType(Type t)
        {
            return t != null &&
              IncludedNamespaces.Any(x => t.FullName.StartsWith(x + ".")) &&
              (t.DeclaringType == null || filterType(t.DeclaringType)) &&
              (t.IsPublic || t.IsNestedPublic) &&
              !typeof(Attribute).IsAssignableFrom(t) &&
              !t.FullName.Contains("<") &&
              (!t.IsGenericType || t.IsEnum);
        }

        static string getTypeScriptString(PropertyInfo info)
        {
            var typeString = getTypesScriptType(info.PropertyType, true);
            var isNullable = info.PropertyType.ToString().Contains("Nullable");
            var isStatic = info.GetAccessors(true)[0].IsStatic;

            return string.Format("{3}{0}{4}: {1};{2}",
              info.Name,
              typeString,
              typeString == "any" ? " // " + info.PropertyType : "",
              isStatic ? "static " : "",
              isNullable ? "?" : ""
            );
        }

        static string getTypeScriptString(FieldInfo info)
        {
            var typeString = getTypesScriptType(info.FieldType, true);
            var isNullable = info.FieldType.ToString().Contains("Nullable");
            var isStatic = info.IsStatic;

            return string.Format("{3}{0}{4}: {1};{2}",
              info.Name,
              typeString,
              typeString == "any" ? " // " + info.FieldType : "",
              isStatic ? "static " : "",
              isNullable ? "?" : ""
            );
        }

        static string getTypeScriptString(IGrouping<string, MethodInfo> list)
        {
            var info = list.First();
            var isStatic = info.IsStatic;
            var types = string.Join(" | ", list.Select(x => "(" + getTypeScriptString(x) + ")"));

            return string.Format("{0}{1}: {2};",
              isStatic ? "static " : "",
              info.Name,
              types
            );
        }

        static string getTypeScriptString(MethodInfo info)
        {
            var retType = getTypesScriptType(info.ReturnType, true);
            var isStatic = info.IsStatic;

            var args = string.Join(", ", info.GetParameters().Select(getTypeScriptString));

            return string.Format("({0}) => {1}",
              args,
              retType
            );
        }

        static string getTypeScriptString(ParameterInfo info)
        {
            var typeString = getTypesScriptType(info.ParameterType, true);

            return string.Format("{0}{2}: {1}",
                info.Name,
                typeString,
                info.IsOptional ? "?" : ""
            );
        }

        static string getTypeScriptValue(object val)
        {
            if (val == null) return "undefined";

            switch (val)
            {
                case string s:
                    return $"'{s.Replace("'", "\\'")}'";
                case int i:
                case uint ui:
                case short sh:
                case ushort ush:
                case float f:
                case double d:
                case ulong ul:
                case decimal dd:
                case byte b:
                    return val.ToString();
                default:
                    return "{}";
            }
        }


        static string getTypesScriptType(Type type, bool withNs)
        {
            var propertyType = type.ToString();

            switch (propertyType)
            {
                case "System.Void":
                    return "void";

                case "System.String":
                    return "string";

                case "System.Single":
                case "System.Double":
                case "System.Int32":
                    return "number";

                case "System.Boolean":
                    return "boolean";

                case "System.Nullable`1[System.Boolean]":
                    return "boolean";

                case "System.Nullable`1[System.Double]":
                case "System.Nullable`1[System.Single]":
                case "System.Nullable`1[System.Int32]":
                    return "number";

                default:
                    break;
            }
            if (!type.IsEnum && propertyType.Contains("`")) return "any";
            if (type.DeclaringType != null)
            {
                var parent = getTypesScriptType(type.DeclaringType, withNs);
                if (parent == "any") return "any";
                return parent + "_" + type.Name;
            }
            if (ExcludedNamespaces.Any(x => propertyType.StartsWith(x + "."))) return "any";
            if (IncludedNamespaces.Any(x => propertyType.StartsWith(x + "."))) return (withNs ? (type.Namespace + ".") : "") + type.Name;

            var importing = ImportNamespaces.FirstOrDefault(x => propertyType.StartsWith(x.Key + "."));
            if (!string.IsNullOrWhiteSpace(importing.Key))
            {
                Imports.Add(importing.Key);
                return (withNs ? (type.Namespace + ".") : "") + type.Name;
            }

            return "any";
        }
    }
}
