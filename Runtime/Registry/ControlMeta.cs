using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

namespace PromptUGUI.Registry
{
    public sealed class ControlMeta
    {
        private readonly Dictionary<string, Action<object, string>> _setters;

        private ControlMeta(Dictionary<string, Action<object, string>> setters)
        {
            _setters = setters;
        }

        public bool HasAttribute(string name) => _setters.ContainsKey(name);

        public void Apply(object instance, string name, string value)
        {
            if (!_setters.TryGetValue(name, out var setter))
                throw new ArgumentException($"unknown attribute '{name}'");
            setter(instance, value);
        }

        public static ControlMeta Build(Type type)
        {
            var setters = new Dictionary<string, Action<object, string>>();

            foreach (var prop in type.GetProperties(
                BindingFlags.Public | BindingFlags.Instance))
            {
                var attr = prop.GetCustomAttribute<UIAttrAttribute>();
                if (attr == null) continue;
                if (!prop.CanWrite) continue;

                var name = attr.Name ?? CamelCase(prop.Name);
                var setter = BuildSetter(prop);
                setters[name] = setter;
            }

            return new ControlMeta(setters);
        }

        private static string CamelCase(string s) =>
            string.IsNullOrEmpty(s) ? s : char.ToLowerInvariant(s[0]) + s.Substring(1);

        private static Action<object, string> BuildSetter(PropertyInfo prop)
        {
            var t = prop.PropertyType;
            if (t == typeof(string))
            {
                return (obj, v) => prop.SetValue(obj, v);
            }
            if (t == typeof(int))
            {
                return (obj, v) => prop.SetValue(obj,
                    int.Parse(v, CultureInfo.InvariantCulture));
            }
            if (t == typeof(float))
            {
                return (obj, v) => prop.SetValue(obj,
                    float.Parse(v, CultureInfo.InvariantCulture));
            }
            if (t == typeof(bool))
            {
                return (obj, v) => prop.SetValue(obj, bool.Parse(v));
            }
            throw new NotSupportedException(
                $"[UIAttr] on {prop.DeclaringType.Name}.{prop.Name}: " +
                $"type {t} not supported in M1");
        }
    }
}
