using DiffHacker.SchemaGen;

var options = Options.Parse(args, out var parseError);
if (options is null)
{
    await Console.Error.WriteLineAsync($"DiffHacker.SchemaGen: {parseError}").ConfigureAwait(false);
    return 2;
}

try
{
    return await new ContractGenerator(options).RunAsync().ConfigureAwait(false);
}
catch (SchemaGenException ex)
{
    await Console.Error.WriteLineAsync($"DiffHacker.SchemaGen: error: {ex.Message}").ConfigureAwait(false);
    return 1;
}
