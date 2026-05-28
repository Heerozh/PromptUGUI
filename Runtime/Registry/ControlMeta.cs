using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

namespace PromptUGUI.Registry
{
    public sealed class ControlMeta
    {
        private readonly Dictionary<string, Action<object, string>> _setters;

        /// <summary>Attribute names (camelCase, matching XML) that carry sprite
        /// references — i.e. were declared with <c>[UIAttr(IsSprite = true)]</c>.
        /// Consumed by the Editor-side <c>SpriteAtlasSyncer</c> so XML refs on
        /// non-`sprite` attribute names (e.g. <c>Progress.fill / bg / frame / mask</c>)
        /// reach the atlas. Empty for controls that don't display sprites.</summary>
        public IReadOnlyCollection<string> SpriteAttrs { get; }

        /// <summary>Attribute names (camelCase, matching XML) that carry color
        /// references — i.e. were declared with <c>[UIAttr(IsColor = true)]</c>.
        /// Consumed by the Editor-side lint pipeline to discover color-bearing
        /// attribute names per control. Empty for controls that don't use colors.</summary>
        public IReadOnlyCollection<string> ColorAttrs { get; }

        private ControlMeta(Dictionary<string, Action<object, string>> setters,
                            IReadOnlyCollection<string> spriteAttrs,
                            IReadOnlyCollection<string> colorAttrs)
        {
            _setters = setters;
            SpriteAttrs = spriteAttrs;
            ColorAttrs = colorAttrs;
        }

        public bool HasAttribute(string name) => _setters.ContainsKey(name);

        public void Apply(object instance, string name, string value)
        {
            if (!_setters.TryGetValue(name, out var setter))
                throw new ArgumentException($"unknown attribute '{name}'");
            try { setter(instance, value); }
            catch (TargetInvocationException tie) when (tie.InnerException != null)
            {
                // 通过反射调用属性 setter 时，setter 抛出的异常被包成 TargetInvocationException。
                // 调用方期待看到原始类型（例如 ParseException），剥一层。
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(tie.InnerException).Throw();
            }
        }

        public static ControlMeta Build(Type type)
        {
            var setters = new Dictionary<string, Action<object, string>>();
            var spriteAttrs = new List<string>();
            var colorAttrs = new List<string>();

            foreach (var prop in type.GetProperties(
                BindingFlags.Public | BindingFlags.Instance))
            {
                var attr = prop.GetCustomAttribute<UIAttrAttribute>();
                if (attr == null) continue;
                if (!prop.CanWrite) continue;

                var name = attr.Name ?? CamelCase(prop.Name);
                var setter = BuildSetter(prop);
                setters[name] = setter;
                if (attr.IsSprite) spriteAttrs.Add(name);
                if (attr.IsColor) colorAttrs.Add(name);
            }

            return new ControlMeta(setters, spriteAttrs, colorAttrs);
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
