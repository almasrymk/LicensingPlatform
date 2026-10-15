using FluentValidation;
using Licensing.BuildingBlocks.Application;
using Licensing.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Licensing.Tests.Unit;

public sealed record Echo(string Text) : ICommand<Result<string>>;
public sealed record Count(int Max) : IQuery<int>;
public sealed record Shout(string Text) : ICommand<string>;

internal sealed class EchoHandler(List<string> trace) : ICommandHandler<Echo, Result<string>>
{
    public Task<Result<string>> HandleAsync(Echo command, CancellationToken ct)
    {
        trace.Add("handler");
        return Task.FromResult<Result<string>>(command.Text);
    }
}

internal sealed class CountHandler : IQueryHandler<Count, int>
{
    public Task<int> HandleAsync(Count query, CancellationToken ct) => Task.FromResult(query.Max);
}

internal sealed class ShoutHandler : ICommandHandler<Shout, string>
{
    public Task<string> HandleAsync(Shout command, CancellationToken ct) => Task.FromResult(command.Text.ToUpperInvariant());
}

internal sealed class EchoValidator : AbstractValidator<Echo>
{
    public EchoValidator() => RuleFor(x => x.Text).NotEmpty().WithMessage("Text is required.");
}

internal sealed class ShoutValidator : AbstractValidator<Shout>
{
    public ShoutValidator() => RuleFor(x => x.Text).NotEmpty();
}

internal sealed class TraceBehavior<TRequest, TResult>(List<string> trace, string name) : IPipelineBehavior<TRequest, TResult> where TRequest : notnull
{
    public async Task<TResult> HandleAsync(TRequest request, HandlerDelegate<TResult> next, CancellationToken ct)
    {
        trace.Add($"{name}:before");
        var result = await next();
        trace.Add($"{name}:after");
        return result;
    }
}

public class DispatcherTests
{
    private static (IDispatcher Dispatcher, List<string> Trace) Build(Action<IServiceCollection>? configure = null)
    {
        var trace = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton(trace);
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        services.AddScoped<ICommandHandler<Echo, Result<string>>, EchoHandler>();
        services.AddScoped<IQueryHandler<Count, int>, CountHandler>();
        services.AddScoped<ICommandHandler<Shout, string>, ShoutHandler>();
        services.AddCqrs();
        configure?.Invoke(services);
        var provider = services.BuildServiceProvider().CreateScope().ServiceProvider;
        return (provider.GetRequiredService<IDispatcher>(), trace);
    }

    [Fact]
    public async Task Command_reaches_its_handler_and_returns_its_result()
    {
        var (dispatcher, _) = Build();
        var result = await dispatcher.SendAsync(new Echo("hi"));
        Assert.True(result.IsSuccess);
        Assert.Equal("hi", result.Value);
    }

    [Fact]
    public async Task Query_reaches_its_handler()
    {
        var (dispatcher, _) = Build();
        Assert.Equal(7, await dispatcher.QueryAsync(new Count(7)));
    }

    [Fact]
    public async Task Behaviors_wrap_the_handler_with_the_first_registered_outermost()
    {
        var (dispatcher, trace) = Build(s =>
        {
            s.AddScoped<IPipelineBehavior<Echo, Result<string>>>(sp => new TraceBehavior<Echo, Result<string>>(sp.GetRequiredService<List<string>>(), "outer"));
            s.AddScoped<IPipelineBehavior<Echo, Result<string>>>(sp => new TraceBehavior<Echo, Result<string>>(sp.GetRequiredService<List<string>>(), "inner"));
        });
        await dispatcher.SendAsync(new Echo("x"));
        Assert.Equal(["outer:before", "inner:before", "handler", "inner:after", "outer:after"], trace);
    }

    [Fact]
    public async Task Invalid_command_returning_a_Result_gets_a_validation_error_and_never_reaches_the_handler()
    {
        var (dispatcher, trace) = Build(s => s.AddScoped<IValidator<Echo>, EchoValidator>());
        var result = await dispatcher.SendAsync(new Echo(""));
        Assert.True(result.IsFailure);
        Assert.Equal("VALIDATION_FAILED", result.Error!.Code);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
        Assert.Contains("Text is required.", result.Error.Message);
        Assert.DoesNotContain("handler", trace);
    }

    [Fact]
    public async Task Invalid_command_with_a_plain_result_throws_a_validation_exception()
    {
        var (dispatcher, _) = Build(s => s.AddScoped<IValidator<Shout>, ShoutValidator>());
        await Assert.ThrowsAsync<ValidationException>(() => dispatcher.SendAsync(new Shout("")));
        Assert.Equal("OK", await dispatcher.SendAsync(new Shout("ok")));
    }

    [Fact]
    public async Task Missing_handler_fails_loudly()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCqrs();
        var dispatcher = services.BuildServiceProvider().GetRequiredService<IDispatcher>();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => dispatcher.QueryAsync(new Count(1)));
        Assert.Contains(nameof(Count), error.Message);
    }

    [Fact]
    public void Registering_a_second_handler_for_the_same_command_fails_at_startup()
    {
        var services = new ServiceCollection();
        services.AddScoped<IQueryHandler<Count, int>, CountHandler>();
        var error = Assert.Throws<InvalidOperationException>(() => services.AddHandlersFrom(typeof(DispatcherTests).Assembly));
        Assert.Contains(nameof(Count), error.Message);
    }
}
