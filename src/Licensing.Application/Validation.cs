using FluentValidation;
using Licensing.Application.Customers;
using Licensing.Application.Identity;
using Licensing.Application.Integrations;
using Licensing.Application.Licensing;
using Licensing.Application.Subscriptions;
using Licensing.Application.Tenants;

namespace Licensing.Application;

internal sealed class LoginValidator : AbstractValidator<LoginRequest>
{
    public LoginValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(256);
    }
}

internal sealed class ClientTokenValidator : AbstractValidator<ClientTokenRequest>
{
    public ClientTokenValidator()
    {
        RuleFor(x => x.ClientId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.ClientSecret).NotEmpty().MaximumLength(256);
    }
}

internal sealed class CreateUserValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8).MaximumLength(256);
        RuleFor(x => x.Role).NotEmpty().Must(r => Domain.Identity.Roles.All.Contains(r)).WithMessage("Unknown role.");
        RuleFor(x => x.CustomerId).NotEmpty().When(x => x.Role == Domain.Identity.Roles.CustomerUser);
    }
}

internal sealed class CreateTenantValidator : AbstractValidator<CreateTenantRequest>
{
    public CreateTenantValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Code).NotEmpty().MaximumLength(32).Matches("^[A-Za-z0-9_-]+$");
        RuleFor(x => x.ContactEmail).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.ContactEmail));
    }
}

internal sealed class SaveCustomerValidator : AbstractValidator<SaveCustomerRequest>
{
    public SaveCustomerValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Phone).MaximumLength(50);
        RuleFor(x => x.Country).MaximumLength(100);
        RuleFor(x => x.TaxNumber).MaximumLength(50);
    }
}

internal sealed class StartSubscriptionValidator : AbstractValidator<StartSubscriptionRequest>
{
    public StartSubscriptionValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.PlanId).NotEmpty();
        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}

internal sealed class ActivateValidator : AbstractValidator<ActivateRequest>
{
    public ActivateValidator()
    {
        RuleFor(x => x.ProductKey).NotEmpty().MaximumLength(64);
        RuleFor(x => x.DeviceId).NotEmpty().MaximumLength(128);
        RuleFor(x => x.DeviceName).MaximumLength(200);
        RuleFor(x => x.AppVersion).MaximumLength(50);
    }
}

internal sealed class ValidateValidator : AbstractValidator<ValidateRequest>
{
    public ValidateValidator()
    {
        RuleFor(x => x.ProductKey).NotEmpty().MaximumLength(64);
        RuleFor(x => x.DeviceId).NotEmpty().MaximumLength(128);
    }
}

internal sealed class CreateApiClientValidator : AbstractValidator<CreateApiClientRequest>
{
    public CreateApiClientValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Scopes).NotEmpty();
    }
}
