// Copyright (c) 2020 brian91292, (c) 2026 L4yLa. MIT License.
// EnhancedStreamChat の変換器を元にしている。
using IPA.Config.Data;
using IPA.Config.Stores;
using System;
using UnityEngine;

namespace TTVStreamersKit.Configuration
{
    /// <summary>色を "#RRGGBBAA" の文字列として保存する。</summary>
    internal sealed class ColorConverterWithAlpha : ValueConverter<Color>
    {
        public override Color FromValue(Value value, object parent)
        {
            if (value is Text t && ColorUtility.TryParseHtmlString(t.Value, out var color)) {
                return color;
            }
            throw new ArgumentException("Value cannot be parsed into a Color.", nameof(value));
        }

        public override Value ToValue(Color obj, object parent) => Value.Text($"#{ColorUtility.ToHtmlStringRGBA(obj)}");
    }

    /// <summary><see cref="Vector3"/> を x / y / z の表として保存する。</summary>
    internal sealed class Vector3Converter : ValueConverter<Vector3>
    {
        public override Vector3 FromValue(Value value, object parent)
        {
            if (!(value is Map m)) {
                throw new ArgumentException("Value cannot be parsed into a Vector3.", nameof(value));
            }
            return new Vector3(Component(m, "x"), Component(m, "y"), Component(m, "z"));
        }

        public override Value ToValue(Vector3 obj, object parent)
        {
            var map = Value.Map();
            map.Add("x", Value.Float((decimal)obj.x));
            map.Add("y", Value.Float((decimal)obj.y));
            map.Add("z", Value.Float((decimal)obj.z));
            return map;
        }

        private static float Component(Map m, string name)
        {
            if (!m.TryGetValue(name, out var v)) {
                return 0f;
            }
            if (v is FloatingPoint f) {
                return (float)f.Value;
            }
            // 小数点のない値は整数として読まれる
            if (v is Integer i) {
                return i.Value;
            }
            return 0f;
        }
    }
}
