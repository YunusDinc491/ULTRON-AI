namespace Ultron.Infrastructure.Ollama;

public sealed class OllamaException(string message) : Exception(message);
