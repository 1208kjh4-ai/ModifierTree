using ModifierTree.Core;
using Rhino.Geometry;

namespace ModifierTree.Rhino.Modifiers;

internal readonly record struct BendControl(Box Box, ControlBoxSettings Settings);

/// <summary>Applies control boxes in tree order to independent copies of the complete input solid set.</summary>
internal static class BendEvaluator
{
    public static Brep[] Evaluate(IReadOnlyList<IReadOnlyList<Brep>> children,
        IReadOnlyList<BendControl> controls, double tolerance)
    {
        if (!double.IsFinite(tolerance) || tolerance <= 0)
            throw new InvalidOperationException("Document tolerance must be positive and finite.");
        if (controls.Count == 0) throw new InvalidOperationException("Add a Control Box to this Bend.");
        if (children.Count == 0) throw new InvalidOperationException("Drag at least one input into this Bend.");
        var results = new List<Brep>();
        try
        {
            foreach (var input in children.SelectMany(child => child))
            {
                if (DifferenceEvaluator.Validate(input, "Bend input") is { } error)
                    throw new InvalidOperationException(error);
                var copy = input.DuplicateBrep();
                results.Add(copy);
                if (copy.SolidOrientation == BrepSolidOrientation.Inward) copy.Flip();
            }
            foreach (var control in controls)
            {
                if (!ModifierSettings.TryValidate(control.Settings, out var error))
                    throw new InvalidOperationException(error);
                var box = control.Box;
                if (!box.IsValid || box.X.Length <= tolerance || box.Y.Length <= tolerance || box.Z.Length <= tolerance)
                    throw new InvalidOperationException("Control Box dimensions must be larger than the document tolerance.");
                if (control.Settings.Strength == 0) continue;
                var frame = ControlBoxGeometry.BendFrame(box);
                var radians = control.Settings.Strength * Math.PI / 180;
                var localTransform = Transform.PlaneToPlane(frame, Plane.WorldXY);
                var bounds = results.Select(result => result.GetBoundingBox(localTransform)).ToArray();
                ValidateBounds(bounds, box.Y.Length, radians, control.Settings.Limited, tolerance);
                var morph = new LengthPreservingBend(frame, box.Y.Length, radians,
                    control.Settings.Limited, tolerance / 10);
                foreach (var result in results)
                {
                    if (!morph.Morph(result) || !result.IsValid || !result.IsSolid ||
                        result.SolidOrientation == BrepSolidOrientation.Inward)
                        throw new InvalidOperationException("Bend could not produce a valid closed solid. Reduce Strength or adjust the Control Box.");
                }
            }
            return results.ToArray();
        }
        catch
        {
            foreach (var result in results) result.Dispose();
            throw;
        }
    }

    private static void ValidateBounds(BoundingBox[] bounds, double height, double radians, bool limited, double tolerance)
    {
        if (bounds.Any(bound => !bound.IsValid)) throw new InvalidOperationException("Cannot determine the Bend input bounds.");
        if (bounds.Length == 0) return;
        var curvature = radians / height;
        foreach (var bound in bounds)
        {
            // Limited's exterior regions are rigid transforms; there is no radial collapse there.
            if (limited && (bound.Max.Y <= 0 || bound.Min.Y >= height)) continue;
            var extremeX = curvature > 0 ? bound.Max.X : bound.Min.X;
            var determinant = 1 - curvature * extremeX;
            if (determinant <= 1e-8 || determinant / Math.Abs(curvature) <= tolerance)
                throw new InvalidOperationException("Bend reaches the curvature center and would collapse or invert the input. Reduce Strength or increase the Control Box height.");
        }
        var ymin = bounds.Min(bound => bound.Min.Y);
        var ymax = bounds.Max(bound => bound.Max.Y);
        var span = limited ? Math.Clamp(ymax, 0, height) - Math.Clamp(ymin, 0, height) : ymax - ymin;
        if (Math.Abs(curvature) * span >= 2 * Math.PI - 1e-8)
            throw new InvalidOperationException("Bend would wrap the inputs through a full turn. Reduce Strength or increase the Control Box height.");
    }
}
