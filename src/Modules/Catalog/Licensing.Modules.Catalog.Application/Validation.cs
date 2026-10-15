using FluentValidation;

namespace Licensing.Modules.Catalog.Application;

internal sealed class SaveProductValidator : AbstractValidator<SaveProductRequest>
{
    public SaveProductValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(32).Matches("^[A-Za-z0-9_-]+$");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(1000);
    }
}

internal sealed class SavePlanValidator : AbstractValidator<SavePlanRequest>
{
    public SavePlanValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.Code).NotEmpty().MaximumLength(32).Matches("^[A-Za-z0-9_-]+$");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Price).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Currency).NotEmpty().Length(3);
        RuleFor(x => x.DurationDays).GreaterThan(0).When(x => x.DurationDays is not null);
        RuleFor(x => x.MaxActivations).GreaterThan(0).When(x => x.MaxActivations is not null);
        RuleFor(x => x.HeartbeatIntervalHours).InclusiveBetween(1, 720);
        RuleFor(x => x.OfflineGraceDays).InclusiveBetween(0, 365);
    }
}
