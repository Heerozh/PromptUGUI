// ---------------------------------------------------------------------------
// Minimal stand-ins for the two UnityEngine value types that the SHARED .pxl
// sources (PxlParser / GplPalette / PxlColorResolver, compiled straight out of
// Editor/Pxl/) reference. They exist so those files compile verbatim outside
// Unity — no #if, no forked copy, no refactor of the Unity-side code.
//
// Contract: keep these byte-for-byte semantically identical to Unity's types
// for the members the shared sources touch:
//     Color32(byte r, byte g, byte b, byte a) + public fields r/g/b/a
//     Vector4(float x, float y, float z, float w) + public fields x/y/z/w
// Nothing else is used, and nothing else should be added here — if a shared
// file starts needing more of UnityEngine, that is the signal to purify the
// file instead of growing the shim.
//
// PxlPreview.csproj strips every inherited <Reference> so the real Unity DLLs
// (pulled in by .lint/Local.props for the Roslyn-workspace projects) can never
// collide with these definitions (CS0433).
// ---------------------------------------------------------------------------

namespace UnityEngine
{
    internal struct Color32
    {
        public byte r, g, b, a;

        public Color32(byte r, byte g, byte b, byte a)
        {
            this.r = r; this.g = g; this.b = b; this.a = a;
        }
    }

    internal struct Vector4
    {
        public float x, y, z, w;

        public Vector4(float x, float y, float z, float w)
        {
            this.x = x; this.y = y; this.z = z; this.w = w;
        }
    }
}
