using TeamStation.Launcher.Validation;

namespace TeamStation.Tests;

/// <summary>
/// Randomized fuzz coverage for <see cref="LaunchInputValidator"/>. The
/// contract invariant is terse but critical: every rejection <b>must</b>
/// surface as <see cref="LaunchValidationException"/>. An unhandled
/// framework exception from deep inside the regex engine, a string
/// indexing mishap, or a <see cref="NullReferenceException"/> would leak
/// out of the launch path and crash the UI — or worse, flow past the
/// validator entirely.
///
/// Fuzz runs use a deterministic seed per test so failures reproduce on
/// the reporting developer's box instead of vanishing on re-run.
/// </summary>
public class ValidatorFuzzTests
{
    private const int FuzzIterations = 10_000;

    // Alphabet stays deliberately weighted toward shapes that historically
    // broke input validators: argv flags, path separators, whitespace,
    // control characters, and obvious SMB/shell smugglers.
    private static readonly char[] FuzzAlphabet =
    [
        // ASCII digits + a few letters so some inputs legitimately match
        // numeric-ID shape.
        '0','1','2','3','4','5','6','7','8','9','a','b','c','d','e','f','-',
        // Flag-looking bytes.
        '-','-','-','-','/','\\',
        // Path separators + known forbidden characters.
        ':','\\','/','"','\'',' ','\t','\r','\n','\0',
        // Surrogate halves produce malformed strings — make sure the
        // validator doesn't blow up on them.
        '\uD800','\uDC00',
        // Right-to-left override and zero-width joiner, chosen because
        // unicode normalisation can erase them in naive implementations.
        '\u202E','\u200D',
    ];

    [Fact]
    public void ValidateTeamViewerId_never_throws_outside_the_contract()
    {
        RunFuzzContract(0xCAFE, rng =>
        {
            var sample = NextFuzzString(rng, minLen: 0, maxLen: 24);
            LaunchInputValidator.ValidateTeamViewerId(sample);
        });
    }

    [Fact]
    public void ValidatePassword_never_throws_outside_the_contract()
    {
        RunFuzzContract(0xFACE, rng =>
        {
            var sample = NextFuzzString(rng, minLen: 0, maxLen: LaunchInputValidator.MaxPasswordLength + 32);
            LaunchInputValidator.ValidatePassword(sample);
        });
    }

    [Fact]
    public void ValidateProxyEndpoint_never_throws_outside_the_contract()
    {
        RunFuzzContract(0xBEEF, rng =>
        {
            var sample = NextFuzzString(rng, minLen: 0, maxLen: 64);
            LaunchInputValidator.ValidateProxyEndpoint(sample);
        });
    }

    [Fact]
    public void ValidateProxyUsername_never_throws_outside_the_contract()
    {
        RunFuzzContract(0xDEAD, rng =>
        {
            var sample = NextFuzzString(rng, minLen: 0, maxLen: LaunchInputValidator.MaxProxyUserLength + 16);
            LaunchInputValidator.ValidateProxyUsername(sample);
        });
    }

    /// <summary>
    /// Accepting predicate — every digits-only input 8-12 chars wide must
    /// validate. Shrinking this contract would break legitimate launches.
    /// </summary>
    [Fact]
    public void ValidateTeamViewerId_accepts_every_purely_numeric_id_in_range()
    {
        var rng = new Random(0xFEED);
        for (var i = 0; i < 2_000; i++)
        {
            var len = rng.Next(8, 13);
            var chars = new char[len];
            for (var c = 0; c < len; c++)
                chars[c] = (char)('0' + rng.Next(0, 10));

            var id = new string(chars);
            LaunchInputValidator.ValidateTeamViewerId(id); // must not throw
        }
    }

    /// <summary>
    /// Coarse sanity — a reasonable fraction of random samples should be
    /// rejected. If it regresses to zero rejections the validator has been
    /// effectively disabled.
    /// </summary>
    [Fact]
    public void ValidatePassword_rejects_a_meaningful_fraction_of_random_inputs()
    {
        var rng = new Random(0xBABE);
        var rejected = 0;
        for (var i = 0; i < FuzzIterations; i++)
        {
            var sample = NextFuzzString(rng, minLen: 0, maxLen: LaunchInputValidator.MaxPasswordLength + 32);
            try { LaunchInputValidator.ValidatePassword(sample); }
            catch (LaunchValidationException) { rejected++; }
        }

        // Alphabet is heavy with forbidden shapes — we expect rejection rate
        // well north of 50% in practice; assert a loose lower bound so flaky
        // stats don't fail the test.
        Assert.InRange(rejected, FuzzIterations / 4, FuzzIterations);
    }

    // ------------------------------------------------------------------

    private static string NextFuzzString(Random rng, int minLen, int maxLen)
    {
        var length = rng.Next(minLen, maxLen + 1);
        if (length == 0) return rng.Next(0, 4) switch
        {
            0 => string.Empty,
            1 => " ",
            2 => "\t",
            _ => "\0",
        };

        var chars = new char[length];
        for (var i = 0; i < length; i++)
            chars[i] = FuzzAlphabet[rng.Next(FuzzAlphabet.Length)];
        return new string(chars);
    }

    /// <summary>
    /// Wraps a fuzz action with the contract: the only acceptable exception
    /// is <see cref="LaunchValidationException"/>. Anything else fails the
    /// test with the offending seed so the failure is reproducible.
    /// </summary>
    private static void RunFuzzContract(int seed, Action<Random> run)
    {
        var rng = new Random(seed);
        for (var i = 0; i < FuzzIterations; i++)
        {
            try
            {
                run(rng);
            }
            catch (LaunchValidationException)
            {
                // expected negative case — continue
            }
            catch (Exception ex)
            {
                Assert.Fail(
                    $"Fuzz iteration {i} with seed 0x{seed:X} leaked a non-contract exception: "
                    + $"{ex.GetType().FullName}: {ex.Message}");
            }
        }
    }
}
