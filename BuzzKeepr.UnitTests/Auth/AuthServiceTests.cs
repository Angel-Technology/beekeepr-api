using BuzzKeepr.Application.Auth;
using BuzzKeepr.Application.Auth.Models;
using BuzzKeepr.Application.Users;
using BuzzKeepr.Domain.Entities;
using BuzzKeepr.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace BuzzKeepr.UnitTests.Auth;

public sealed class AuthServiceTests
{
    private readonly IAuthRepository repository = Substitute.For<IAuthRepository>();
    private readonly IEmailSignInSender emailSender = Substitute.For<IEmailSignInSender>();
    private readonly IGoogleTokenVerifier googleVerifier = Substitute.For<IGoogleTokenVerifier>();
    private readonly IWelcomeEmailSender welcomeSender = Substitute.For<IWelcomeEmailSender>();
    private readonly AuthService sut;

    public AuthServiceTests()
    {
        sut = new AuthService(repository, emailSender, googleVerifier, welcomeSender, NullLogger<AuthService>.Instance);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RequestEmailSignIn_WithBlankEmail_ReturnsEmailRequired(string email)
    {
        var result = await sut.RequestEmailSignInAsync(new RequestEmailSignInInput { Email = email }, default);

        Assert.True(result.EmailRequired);
        Assert.False(result.Success);
        await emailSender.DidNotReceive().SendSignInCodeAsync(default!, default!, default, default);
    }

    [Fact]
    public async Task RequestEmailSignIn_WhenEmailDeliveryThrows_ReturnsEmailDeliveryFailed()
    {
        emailSender.SendSignInCodeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("resend down"));

        var result = await sut.RequestEmailSignInAsync(new RequestEmailSignInInput { Email = "user@example.com" }, default);

        Assert.True(result.EmailDeliveryFailed);
        Assert.False(result.Success);
        await repository.DidNotReceive().AddVerificationTokenAsync(Arg.Any<VerificationToken>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RequestEmailSignIn_NormalizesEmailBeforeRepoLookup()
    {
        await sut.RequestEmailSignInAsync(new RequestEmailSignInInput { Email = "  Mixed.Case@Example.COM  " }, default);

        await repository.Received(1).GetUserByEmailAsync("mixed.case@example.com", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SignInWithGoogle_WithBlankIdToken_ReturnsInvalidInput()
    {
        var result = await sut.SignInWithGoogleAsync(new SignInWithGoogleInput { IdToken = "" }, default);

        Assert.True(result.InvalidInput);
        await googleVerifier.DidNotReceive().VerifyIdTokenAsync(default!, default);
    }

    [Fact]
    public async Task SignInWithGoogle_WhenVerifierReturnsNull_ReturnsInvalidToken()
    {
        googleVerifier.VerifyIdTokenAsync("rejected-token", Arg.Any<CancellationToken>())
            .Returns((GoogleIdentity?)null);

        var result = await sut.SignInWithGoogleAsync(new SignInWithGoogleInput { IdToken = "rejected-token" }, default);

        Assert.True(result.InvalidToken);
        Assert.False(result.Success);
    }
}
