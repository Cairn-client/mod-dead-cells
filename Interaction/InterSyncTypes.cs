namespace DeadCellsMultiplayerMod.Interaction;

public readonly struct InterDoorEvent
{
    public readonly int UserId;
    public readonly double X;
    public readonly double Y;
    public readonly string Action;
    public readonly bool Broken;

    public InterDoorEvent(int userId, double x, double y, string action, bool broken)
    {
        UserId = userId;
        X = x;
        Y = y;
        Action = action ?? string.Empty;
        Broken = broken;
    }
}

public readonly struct InterElevatorEvent
{
    public readonly double X;
    public readonly double Y;

    public InterElevatorEvent(double x, double y)
    {
        X = x;
        Y = y;
    }
}

public readonly struct InterPressurePlateEvent
{
    public readonly double X;
    public readonly double Y;

    public InterPressurePlateEvent(double x, double y)
    {
        X = x;
        Y = y;
    }
}
