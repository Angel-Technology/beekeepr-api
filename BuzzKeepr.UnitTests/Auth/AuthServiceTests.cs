using System.Security.Cryptography;
using System.Text;
using BuzzKeepr.Application.Auth;
using BuzzKeepr.Application.Auth.Models;
using BuzzKeepr.Application.Users;
using BuzzKeepr.Domain.Entities;
using BuzzKeepr.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace BuzzKeepr.UnitTests.Auth;

public sealed class AuthServiceTests
{
    private readonly IAuthRepository repository = Substitute.For<IAuthRepository>();
    private readonly IEmailSignInSender emailSender = Substitute.For<IEmailSignInSender>();
    private readonly IGoogleTokenVerifier googleVerifier = Substitute.For<IGoogleTokenVerifier>();
    private readonly IWelcomeEmailSender welcomeSender = Substitute.For<IWelcomeEmailSender>();
    private readonly AuthOptions authOptions = new();
    private AuthService sut;

    public AuthServiceTests()
    {
        sut = BuildSut();
    }

    private AuthService BuildSut() => new(
        repository,
        emailSender,
        googleVerifier,
        welcomeSender,
        Options.Create(authOptions),
        NullLogger<AuthService>.Instance);

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
    public async Task RequestEmailSignIn_ForReviewAccount_SkipsEmailSendButPersistsToken()
    {
        authOptions.ReviewAccounts.Add(new ReviewAccount { Email = "apple-review@buzzkeepr.com", Pin = "424242" });
        sut = BuildSut();

        VerificationToken? captured = null;
        await repository.AddVerificationTokenAsync(
            Arg.Do<VerificationToken>(t => captured = t),
            Arg.Any<CancellationToken>());

        var result = await sut.RequestEmailSignInAsync(
            new RequestEmailSignInInput { Email = "apple-review@buzzkeepr.com" },
            default);

        Assert.True(result.Success);
        await emailSender.DidNotReceiveWithAnyArgs().SendSignInCodeAsync(default!, default!, default, default);
        Assert.NotNull(captured);
        Assert.Equal(HashTokenForTest("424242"), captured!.TokenHash);
    }

    private static string HashTokenForTest(string raw)
    {
        // Mirrors AuthService.HashToken so the test doesn't carry a hardcoded SHA256 literal
        // that would drift if the hashing scheme ever changed.
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    }

    [Fact]
    public async Task RequestEmailSignIn_ForReviewAccount_VerifiesWithConfiguredPin()
    {
        authOptions.ReviewAccounts.Add(new ReviewAccount { Email = "apple-review@buzzkeepr.com", Pin = "424242" });
        sut = BuildSut();

        VerificationToken? persisted = null;
        await repository.AddVerificationTokenAsync(
            Arg.Do<VerificationToken>(t => persisted = t),
            Arg.Any<CancellationToken>());

        await sut.RequestEmailSignInAsync(
            new RequestEmailSignInInput { Email = "apple-review@buzzkeepr.com" },
            default);

        Assert.NotNull(persisted);
        repository.GetValidVerificationTokenAsync(
                "apple-review@buzzkeepr.com",
                VerificationTokenPurpose.EmailSignIn,
                Arg.Any<DateTime>(),
                Arg.Any<CancellationToken>())
            .Returns(persisted);

        var result = await sut.VerifyEmailSignInAsync(
            new VerifyEmailSignInInput { Email = "apple-review@buzzkeepr.com", Code = "424242" },
            default);

        Assert.True(result.Success);
        Assert.False(result.InvalidToken);
        Assert.NotNull(result.SessionToken);
    }

    [Fact]
    public async Task RequestEmailSignIn_ForReviewAccount_IsCaseInsensitiveOnEmail()
    {
        authOptions.ReviewAccounts.Add(new ReviewAccount { Email = "apple-review@buzzkeepr.com", Pin = "424242" });
        sut = BuildSut();

        var result = await sut.RequestEmailSignInAsync(
            new RequestEmailSignInInput { Email = "  Apple-Review@BuzzKeepr.COM  " },
            default);

        Assert.True(result.Success);
        await emailSender.DidNotReceiveWithAnyArgs().SendSignInCodeAsync(default!, default!, default, default);
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
