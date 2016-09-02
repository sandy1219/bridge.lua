using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

using ICSharpCode.NRefactory.TypeSystem;
using System.Xml.Serialization;
using Mono.Cecil;

namespace Bridge.Contract {
    public sealed class RawString {
        public string s_;

        public RawString(string s) {
            s_ = s;
        }

        public override string ToString() {
            return s_;
        }
    }

    public static class TransformCtx {
        public const string DefaultString = "__default__";
        public const string DefaultInvoke = DefaultString + "()";

        public static T GetOrDefault<K, T>(this IDictionary<K, T> dict, K key, T t = default(T)) {
            T v;
            if(dict.TryGetValue(key, out v)) {
                return v;
            }
            return t;
        }

        public sealed class MethodInfo {
            public string Name;
            public bool IsPrivate;
            public bool IsCtor;
        }

        public static readonly HashSet<string> CurUsingNamespaces = new HashSet<string>();
        public static Func<IEntity, string> GetEntityName;
        public static List<MethodInfo> CurClassMethodNames = new List<MethodInfo>();
        public static IType CurClass;
        public static List<string> CurClassOtherMethods = new List<string>();
        public static List<MethodInfo> CurClassOtherMethodNames = new List<MethodInfo>();
        public static Dictionary<ITypeInfo, string> NamespaceNames = new Dictionary<ITypeInfo, string>();
        public static HashSet<IType> ExportEnums = new HashSet<IType>();
    }

    [XmlRoot("assembly")]
    public sealed class XmlMetaModel {
        public sealed class TemplateModel {
            [XmlAttribute]
            public string Template;
        }

        public sealed class PropertyModel {
            [XmlAttribute]
            public string name;
            [XmlElement]
            public TemplateModel set;
            [XmlElement]
            public TemplateModel get;
        }

        public sealed class ClassModel {
            [XmlAttribute]
            public string name;
            [XmlAttribute]
            public string Name;
            [XmlAttribute]
            public bool IsSingleCtor;
            [XmlElement("property")]
            public PropertyModel[] Propertys;
        }

        public sealed class NamespaceModel {
            [XmlAttribute]
            public string name;
            [XmlAttribute]
            public string Name;
            [XmlElement("class")]
            public ClassModel[] Classes;
        }

        [XmlElement("namespace")]
        public NamespaceModel[] Namespaces;
    }

    public sealed class XmlMetaMaker {
        public sealed class TypeMetaInfo {
            private XmlMetaModel.ClassModel model_;
            public TypeDefinition TypeDefinition { get; private set; }

            public TypeMetaInfo(TypeDefinition typeDefinition, XmlMetaModel.ClassModel model) {
                TypeDefinition = typeDefinition;
                model_ = model;
                Property();
            }

            public string Name {
                get {
                    return model_.Name;
                }
            }

            public bool IsSingleCtor {
                get {
                    return model_.IsSingleCtor;
                }
            }

            private void Property() {
                if(model_.Propertys != null) {
                    foreach(var propertyModel in model_.Propertys) {
                        PropertyDefinition propertyDefinition = TypeDefinition.Properties.First(i => i.Name == propertyModel.name);
                        PropertyMataInfo info = new PropertyMataInfo(propertyDefinition, propertyModel);
                        XmlMetaMaker.AddProperty(info);
                    }
                }
            }
        }

        public sealed class PropertyMataInfo {
            private XmlMetaModel.PropertyModel model_;
            public PropertyDefinition PropertyDefinition { get; private set; }

            public PropertyMataInfo(PropertyDefinition propertyDefinition, XmlMetaModel.PropertyModel model) {
                PropertyDefinition = propertyDefinition;
                model_ = model;
            }

            public string GetTemplate(bool isGet) {
                var model = isGet ? model_.get : model_.set;
                return model.Template;
            }
        }

        private static Dictionary<TypeDefinition, TypeMetaInfo> types_ = new Dictionary<TypeDefinition, TypeMetaInfo>();
        private static Dictionary<PropertyDefinition, PropertyMataInfo> propertys_ = new Dictionary<PropertyDefinition, PropertyMataInfo>();
        private static Dictionary<string, string> namespaceMaps_ = new Dictionary<string, string>();
        private static IEmitter emitter_;

        public static void Load(IEnumerable<string> files, IEmitter emitter) {
            foreach(string file in files) {
                XmlSerializer xmlSeliz = new XmlSerializer(typeof(XmlMetaModel));
                try {
                    using(Stream stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read)) {
                        XmlMetaModel model = (XmlMetaModel)xmlSeliz.Deserialize(stream);
                        if(model.Namespaces != null) {
                            foreach(var namespaceModel in model.Namespaces) {
                                Load(namespaceModel, emitter);
                            }
                        }
                    }
                }
                catch(Exception e) {
                    throw new Exception("load xml file wrong at " + file, e);
                }
            }
        }

        private static void FixName(ref string name) {
            name = name.Replace('^', '`');
        }

        private static void Load(XmlMetaModel.NamespaceModel model, IEmitter emitter) {
            emitter_ = emitter;

            string namespaceName = model.name;
            if(string.IsNullOrEmpty(namespaceName)) {
                throw new ArgumentException("namespace's name is empty");
            }

            if(!string.IsNullOrEmpty(model.Name)) {
                if(namespaceMaps_.ContainsKey(namespaceName)) {
                    throw new ArgumentException(namespaceName + " namespace map is already has");
                }
                namespaceMaps_.Add(namespaceName, model.Name);
            }

            if(model.Classes != null) {
                foreach(var classModel in model.Classes) {
                    string className = classModel.name;
                    if(string.IsNullOrEmpty(className)) {
                        throw new ArgumentException(string.Format("namespace[{0}] has a class's name is empty", namespaceName));
                    }

                    string keyName = namespaceName + '.' + className;
                    FixName(ref keyName);
                    var type = emitter.BridgeTypes.GetOrDefault(keyName);
                    if(type == null) {
                        throw new ArgumentException(string.Format("keyName[{0}] is not found", keyName));
                    }

                    if(types_.ContainsKey(type.TypeDefinition)) {
                        throw new ArgumentException(type.TypeDefinition.FullName + " is already has");
                    }

                    TypeMetaInfo info = new TypeMetaInfo(type.TypeDefinition, classModel);
                    types_.Add(info.TypeDefinition, info);
                }
            }
        }

        public static bool IsSingleCtor(TypeDefinition type) {
            var info = types_.GetOrDefault(type);
            return info != null && info.IsSingleCtor;
        }

        public static string GetCustomName(TypeDefinition type) {
            var info = types_.GetOrDefault(type);
            return info != null ? info.Name : null;
        }

        private static void AddProperty(PropertyMataInfo info) {
            if(propertys_.ContainsKey(info.PropertyDefinition)) {
                throw new ArgumentException(info.PropertyDefinition.FullName + " is already has");
            }
            propertys_.Add(info.PropertyDefinition, info);
        }

        public static string GetPropertyInline(IProperty property, bool isGet) {
            var type = emitter_.BridgeTypes.Get(property.MemberDefinition.DeclaringType);
            PropertyDefinition propertyDefinition = type.TypeDefinition.Properties.First(i => i.Name == property.Name);
            var info = propertys_.GetOrDefault(propertyDefinition);
            return info != null ? info.GetTemplate(isGet) : null;

        }

        public static string GetNamespace(string name) {
            return namespaceMaps_.GetOrDefault(name, name);
        }
    }
}
