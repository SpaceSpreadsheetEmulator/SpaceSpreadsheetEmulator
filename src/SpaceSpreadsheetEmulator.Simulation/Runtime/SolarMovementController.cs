namespace SpaceSpreadsheetEmulator.Simulation.Runtime;

/// <summary>
/// Resolves authoritative ship velocities from persisted player movement intent.
/// </summary>
internal static class SolarMovementController
{
    public static bool TryResolveVelocity(
        SolarShipState ship,
        SolarMovementIntent intent,
        IReadOnlyDictionary<long, SolarVector3> targetPositions,
        out SolarVector3 velocity)
    {
        double maximumVelocity = ship.MovementProfile.MaximumVelocity;
        switch (intent.Kind)
        {
            case SolarMovementIntentKind.Direction:
                velocity = Scale(intent.Direction, Math.Min(intent.RequestedSpeed, maximumVelocity));
                return true;
            case SolarMovementIntentKind.Stop:
                velocity = SolarVector3.Zero;
                return true;
            case SolarMovementIntentKind.GoToPoint:
                velocity = MoveToward(
                    ship.Position,
                    intent.TargetPosition!.Value,
                    desiredRange: 0,
                    maximumVelocity);
                return true;
            case SolarMovementIntentKind.Follow:
                return TryResolveTargetedVelocity(
                    ship,
                    intent,
                    targetPositions,
                    maximumVelocity,
                    orbit: false,
                    out velocity);
            case SolarMovementIntentKind.Orbit:
                return TryResolveTargetedVelocity(
                    ship,
                    intent,
                    targetPositions,
                    maximumVelocity,
                    orbit: true,
                    out velocity);
            default:
                throw new InvalidOperationException("The movement intent kind is unsupported.");
        }
    }

    private static bool TryResolveTargetedVelocity(
        SolarShipState ship,
        SolarMovementIntent intent,
        IReadOnlyDictionary<long, SolarVector3> targetPositions,
        double maximumVelocity,
        bool orbit,
        out SolarVector3 velocity)
    {
        if (!targetPositions.TryGetValue(intent.TargetEntityId!.Value, out SolarVector3 targetPosition))
        {
            velocity = SolarVector3.Zero;
            return false;
        }

        velocity = orbit
            ? ResolveOrbitVelocity(ship.Position, targetPosition, intent.DesiredRange, maximumVelocity)
            : MoveToward(ship.Position, targetPosition, intent.DesiredRange, maximumVelocity);
        return true;
    }

    private static SolarVector3 MoveToward(
        SolarVector3 position,
        SolarVector3 target,
        double desiredRange,
        double maximumVelocity)
    {
        SolarVector3 offset = Subtract(target, position);
        double distance = Magnitude(offset);
        double remaining = distance - desiredRange;
        if (remaining <= 0 || distance == 0)
        {
            return SolarVector3.Zero;
        }

        return Scale(offset, Math.Min(maximumVelocity, remaining) / distance);
    }

    private static SolarVector3 ResolveOrbitVelocity(
        SolarVector3 position,
        SolarVector3 target,
        double desiredRange,
        double maximumVelocity)
    {
        SolarVector3 offset = Subtract(target, position);
        double distance = Magnitude(offset);
        SolarVector3 radial = distance == 0
            ? new SolarVector3(1, 0, 0)
            : Scale(offset, 1 / distance);
        SolarVector3 tangent = Normalize(new SolarVector3(-radial.Y, radial.X, 0));
        if (tangent == SolarVector3.Zero)
        {
            tangent = new SolarVector3(0, 1, 0);
        }

        double radialSpeed = Math.Clamp(
            distance - desiredRange,
            -maximumVelocity / 2,
            maximumVelocity / 2);
        double tangentialSpeed = Math.Sqrt(
            Math.Max(0, (maximumVelocity * maximumVelocity) - (radialSpeed * radialSpeed)));
        return Add(Scale(radial, radialSpeed), Scale(tangent, tangentialSpeed));
    }

    private static SolarVector3 Normalize(SolarVector3 vector)
    {
        double magnitude = Magnitude(vector);
        return magnitude == 0 ? SolarVector3.Zero : Scale(vector, 1 / magnitude);
    }

    private static double Magnitude(SolarVector3 vector)
        => Math.Sqrt(
            (vector.X * vector.X)
            + (vector.Y * vector.Y)
            + (vector.Z * vector.Z));

    private static SolarVector3 Add(SolarVector3 left, SolarVector3 right)
        => new(left.X + right.X, left.Y + right.Y, left.Z + right.Z);

    private static SolarVector3 Subtract(SolarVector3 left, SolarVector3 right)
        => new(left.X - right.X, left.Y - right.Y, left.Z - right.Z);

    private static SolarVector3 Scale(SolarVector3 vector, double scalar)
        => new(vector.X * scalar, vector.Y * scalar, vector.Z * scalar);
}
