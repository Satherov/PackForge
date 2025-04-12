namespace PackForge.Core.Terminal.Arguments;

public abstract class ArgumentType<T>
{
    public abstract T Parse(string token);
}