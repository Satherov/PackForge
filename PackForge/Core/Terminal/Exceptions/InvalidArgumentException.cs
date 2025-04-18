using System;

namespace PackForge.Core.Terminal.Exceptions;

public class InvalidArgumentException : Exception
{
    public InvalidArgumentException() { }
    
    public InvalidArgumentException(string message) : base(message) { }
    
    public InvalidArgumentException(string message, Exception inner) : base(message, inner) { }
    
    public InvalidArgumentException(string message, string argumentName) : base($"{message} Argument: {argumentName}") { }
    
    public InvalidArgumentException(string message, string argumentName, Exception inner) : base($"{message} Argument: {argumentName}", inner) { }
}