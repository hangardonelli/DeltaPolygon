namespace DeltaPolygon.Utilities;

/// <summary>
/// Type of temporal function for serialization
/// </summary>
public enum TemporalFunctionType
{
    /// <summary>
    /// Linear function: x(t) = x0 + vx * (t - t0), y(t) = y0 + vy * (t - t0)
    /// </summary>
    Linear,
    
    /// <summary>
    /// Circular function: x(t) = cx + r * cos(ω * (t - t0) + φ), y(t) = cy + r * sin(ω * (t - t0) + φ)
    /// </summary>
    Circular,
    
    /// <summary>
    /// Custom function (not serializable)
    /// </summary>
    Custom
}

/// <summary>
/// Represents a predictable temporal function for vertex movement
/// 

/// If a vertex follows a predictable pattern, the function is stored instead of each individual state.
/// </summary>
public class TemporalFunction
{
    private readonly Func<DateTime, double> _xFunction;
    private readonly Func<DateTime, double> _yFunction;
    
    /// <summary>
    /// Reference point (origin of the function)
    /// </summary>
    public Models.Point ReferencePoint { get; }
    
    /// <summary>
    /// Reference time (t0)
    /// </summary>
    public DateTime ReferenceTime { get; }
    
    /// <summary>
    /// Type of temporal function for serialization
    /// </summary>
    public TemporalFunctionType FunctionType { get; }
    
    /// <summary>
    /// Function parameters (depend on type):
    /// - Linear: [velocityX, velocityY]
    /// - Circular: [radius, angularVelocity, phase]
    /// </summary>
    public double[] Parameters { get; }

    /// <summary>
    /// Creates a temporal function with custom functions for x and y
    /// </summary>
    public TemporalFunction(
        Models.Point referencePoint,
        DateTime referenceTime,
        Func<DateTime, double> xFunction,
        Func<DateTime, double> yFunction)
    {
        ReferencePoint = referencePoint;
        ReferenceTime = referenceTime;
        _xFunction = xFunction;
        _yFunction = yFunction;
        FunctionType = TemporalFunctionType.Custom;
        Parameters = Array.Empty<double>();
    }
    
    /// <summary>
    /// Internal constructor for serializable functions
    /// </summary>
    private TemporalFunction(
        Models.Point referencePoint,
        DateTime referenceTime,
        Func<DateTime, double> xFunction,
        Func<DateTime, double> yFunction,
        TemporalFunctionType functionType,
        double[] parameters)
    {
        ReferencePoint = referencePoint;
        ReferenceTime = referenceTime;
        _xFunction = xFunction;
        _yFunction = yFunction;
        FunctionType = functionType;
        Parameters = parameters;
    }

    /// <summary>
    /// Creates a linear temporal function (constant movement)
    /// x(t) = x0 + vx * (t - t0)
    /// y(t) = y0 + vy * (t - t0)
    /// </summary>
    public static TemporalFunction CreateLinear(
        Models.Point startPoint,
        DateTime startTime,
        double velocityX,
        double velocityY)
    {
        return new TemporalFunction(
            startPoint,
            startTime,
            t => startPoint.X + velocityX * (t - startTime).TotalSeconds,
            t => startPoint.Y + velocityY * (t - startTime).TotalSeconds,
            TemporalFunctionType.Linear,
            new[] { velocityX, velocityY }
        );
    }

    /// <summary>
    /// Creates a circular temporal function
    /// x(t) = cx + r * cos(ω * (t - t0) + φ)
    /// y(t) = cy + r * sin(ω * (t - t0) + φ)
    /// </summary>
    public static TemporalFunction CreateCircular(
        Models.Point center,
        DateTime startTime,
        double radius,
        double angularVelocity, // radians per second
        double phase = 0)
    {
        return new TemporalFunction(
            center,
            startTime,
            t => center.X + radius * Math.Cos(angularVelocity * (t - startTime).TotalSeconds + phase),
            t => center.Y + radius * Math.Sin(angularVelocity * (t - startTime).TotalSeconds + phase),
            TemporalFunctionType.Circular,
            new[] { radius, angularVelocity, phase }
        );
    }

    /// <summary>
    /// Reconstructs a temporal function from its serialized parameters
    /// </summary>
    /// <param name="functionType">Function type</param>
    /// <param name="referencePoint">Reference point</param>
    /// <param name="referenceTime">Reference time</param>
    /// <param name="parameters">Function parameters</param>
    /// <returns>Reconstructed temporal function, or null if type is Custom</returns>
    public static TemporalFunction? FromParameters(
        TemporalFunctionType functionType,
        Models.Point referencePoint,
        DateTime referenceTime,
        double[] parameters)
    {
        return functionType switch
        {
            TemporalFunctionType.Linear when parameters.Length >= 2 =>
                CreateLinear(referencePoint, referenceTime, parameters[0], parameters[1]),
            
            TemporalFunctionType.Circular when parameters.Length >= 3 =>
                CreateCircular(referencePoint, referenceTime, parameters[0], parameters[1], parameters[2]),
            
            _ => null // Custom or insufficient parameters
        };
    }

    /// <summary>
    /// Evaluates the function at a specific time
    /// </summary>
    public Models.Point Evaluate(DateTime time)
    {
        var deltaX = _xFunction(time) - ReferencePoint.X;
        var deltaY = _yFunction(time) - ReferencePoint.Y;
        return new Models.Point(deltaX, deltaY);
    }

    /// <summary>
    /// Gets the absolute position at a specific time
    /// </summary>
    public Models.Point GetPosition(DateTime time)
    {
        return ReferencePoint + Evaluate(time);
    }
}
