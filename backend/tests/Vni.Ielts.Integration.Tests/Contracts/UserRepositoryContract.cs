using Vni.Ielts.Application.Identity;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Identity;

namespace Vni.Ielts.Integration.Tests.Contracts;

/// <summary>
/// F3.1 — what <see cref="IUserRepository"/> promises, written once, for
/// every provider that will ever implement it.
///
/// <b>The problem this exists to solve.</b> ADR-0003 commits this product to
/// MongoDB now and PostgreSQL later, and ADR-0004 keeps the blast radius of
/// that move to one project by putting the ports in Application and the
/// implementations in Infrastructure. That boundary is enforced
/// (<c>PersistenceBoundaryTests</c>) and the shapes crossing it are enforced
/// (<c>PersistenceRepresentationTests</c>) — but neither says what the port
/// is supposed to <i>do</i>. Every existing repository test resolves the
/// interface from a DI container that only ever registers <c>Mongo*</c>, in a
/// <c>sealed</c> class, so the day a second provider arrives there is nothing
/// to re-run against it: the behaviour would have to be re-specified from
/// scratch, by reading the Mongo implementation and guessing which parts were
/// the contract and which were the driver.
///
/// <b>So the assertions live here and the provider is a hole.</b> A
/// <c>PostgresUserRepository</c> gets a subclass overriding
/// <see cref="CreateAsync"/> and inherits every test below unchanged. If it
/// passes them it is substitutable; if it does not, the difference is named.
///
/// <b>What belongs in here, and what does not.</b> Only promises a caller
/// relies on and any store could keep — an insert is readable afterwards, a
/// lookup that misses returns null rather than throwing, a duplicate email is
/// refused as <see cref="DuplicateEmailException"/> rather than as whatever
/// the driver raised, a save overwrites rather than inserting a second row,
/// search and paging bound their results. Anything Mongo-shaped — a
/// <c>MongoWriteException</c> category, an index name, a BSON document —
/// stays in the Mongo-specific tests, because a Postgres implementation would
/// rightly fail it while being perfectly correct.
///
/// <b>Every test isolates itself by a unique handle rather than by truncating.</b>
/// A contract suite that wipes the store cannot run against a shared database,
/// and demanding a private one is a requirement on the provider that has
/// nothing to do with the contract.
/// </summary>
public abstract class UserRepositoryContract
{
    /// <summary>The implementation under test. One override per provider.</summary>
    protected abstract IUserRepository Repository { get; }

    /// <summary>
    /// Whether this provider's backing store is reachable, and what to say if
    /// not. Kept abstract so a provider that needs no server (an in-memory
    /// fake) simply returns true rather than inheriting a Mongo probe.
    /// </summary>
    protected abstract bool ProviderAvailable { get; }

    protected abstract string ProviderSkipReason { get; }

    private static Email UniqueEmail() =>
        Email.Create($"contract-{Guid.NewGuid():n}@example.com");

    /// <summary>
    /// A number no other test is using.
    ///
    /// <para>
    /// Nine digits behind the `09` trunk prefix, drawn at random rather than
    /// from a counter, because these tests run against a shared database and a
    /// counter would collide across runs — and a collision on a unique index
    /// reads as a failed assertion about the contract rather than as the test
    /// fixture's own doing.
    /// </para>
    /// </summary>
    private static PhoneNumber UniquePhone() =>
        PhoneNumber.Create($"09{Random.Shared.NextInt64(0, 100_000_000):D8}");

    private static readonly DateTimeOffset At =
        new(2026, 9, 8, 9, 0, 0, TimeSpan.Zero);

    private async Task<User> AddOneAsync(string displayName = "Contract Fixture")
    {
        var user = User.Register(UniquePhone(), displayName, At);
        user.ChangeEmail(UniqueEmail(), hasLinkedProvider: false);
        await Repository.AddAsync(user, default);
        return user;
    }

    [SkippableFact]
    public async Task An_added_user_is_found_by_its_id()
    {
        Skip.IfNot(ProviderAvailable, ProviderSkipReason);

        var user = await AddOneAsync();

        var found = await Repository.FindByIdAsync(user.Id, default);

        Assert.NotNull(found);
        Assert.Equal(user.Id, found.Id);
        Assert.Equal(user.Email, found.Email);
        Assert.Equal(user.Phone, found.Phone);
    }

    [SkippableFact]
    public async Task An_added_user_is_found_by_its_email()
    {
        Skip.IfNot(ProviderAvailable, ProviderSkipReason);

        var user = await AddOneAsync();

        var found = await Repository.FindByEmailAsync(user.Email!.Value, default);

        Assert.NotNull(found);
        Assert.Equal(user.Id, found.Id);
    }

    [SkippableFact]
    public async Task An_added_user_is_found_by_its_phone()
    {
        // The lookup most accounts actually depend on: registration collects a
        // number and no address, so this is the only handle they have.
        Skip.IfNot(ProviderAvailable, ProviderSkipReason);

        var user = await AddOneAsync();

        var found = await Repository.FindByPhoneAsync(user.Phone!.Value, default);

        Assert.NotNull(found);
        Assert.Equal(user.Id, found.Id);
    }

    [SkippableFact]
    public async Task A_lookup_that_matches_nothing_returns_null_rather_than_throwing()
    {
        // The single most-relied-on promise in the interface: every caller
        // branches on null. A provider that threw instead would turn "no such
        // account" into a 500 at every one of those call sites.
        Skip.IfNot(ProviderAvailable, ProviderSkipReason);

        Assert.Null(await Repository.FindByIdAsync(UserId.New(), default));
        Assert.Null(await Repository.FindByEmailAsync(UniqueEmail(), default));
        Assert.Null(await Repository.FindByPhoneAsync(UniquePhone(), default));
    }

    [SkippableFact]
    public async Task EmailExists_answers_true_only_after_the_account_is_added()
    {
        Skip.IfNot(ProviderAvailable, ProviderSkipReason);

        var email = UniqueEmail();
        Assert.False(await Repository.EmailExistsAsync(email, default));

        var user = User.Register(UniquePhone(), "Contract Fixture", At);
        user.ChangeEmail(email, hasLinkedProvider: false);
        await Repository.AddAsync(user, default);

        Assert.True(await Repository.EmailExistsAsync(email, default));
    }

    [SkippableFact]
    public async Task PhoneExists_answers_true_only_after_the_account_is_added()
    {
        Skip.IfNot(ProviderAvailable, ProviderSkipReason);

        var phone = UniquePhone();
        Assert.False(await Repository.PhoneExistsAsync(phone, default));

        await Repository.AddAsync(User.Register(phone, "Contract Fixture", At), default);

        Assert.True(await Repository.PhoneExistsAsync(phone, default));
    }

    [SkippableFact]
    public async Task A_second_account_on_the_same_email_is_refused_as_a_duplicate()
    {
        // <b>The contract is the exception TYPE, not the refusal.</b> Any store
        // will refuse this somehow; the promise is that the caller sees
        // DuplicateEmailException and can render its 409, rather than seeing
        // whatever the driver threw and rendering a 500. That translation is
        // exactly the kind of thing a second provider forgets.
        Skip.IfNot(ProviderAvailable, ProviderSkipReason);

        var email = UniqueEmail();

        var first = User.Register(UniquePhone(), "First", At);
        first.ChangeEmail(email, hasLinkedProvider: false);
        await Repository.AddAsync(first, default);

        var second = User.Register(UniquePhone(), "Second", At);
        second.ChangeEmail(email, hasLinkedProvider: false);

        await Assert.ThrowsAsync<DuplicateEmailException>(
            () => Repository.AddAsync(second, default));
    }

    [SkippableFact]
    public async Task A_second_account_on_the_same_phone_is_refused_as_a_duplicate()
    {
        // Same promise as the address, and now the one registration actually
        // hits — <b>and the exception type has to differ</b>, or a learner
        // whose number is taken is told their email address is registered on a
        // form that has no email field.
        Skip.IfNot(ProviderAvailable, ProviderSkipReason);

        var phone = UniquePhone();
        await Repository.AddAsync(User.Register(phone, "First", At), default);

        await Assert.ThrowsAsync<DuplicatePhoneException>(
            () => Repository.AddAsync(User.Register(phone, "Second", At), default));
    }

    [SkippableFact]
    public async Task Moving_a_phone_onto_an_account_that_already_has_it_is_refused_as_a_duplicate()
    {
        /*
         * <b>The replace path, which translated nothing until 08/09/2026.</b>
         * The insert path has always turned a duplicate key into a domain
         * exception; `SaveAsync` did not, so a race between two profile edits
         * surfaced as a raw driver error and a 500. `ChangeEmail` even carried
         * a catch for it that could never fire.
         */
        Skip.IfNot(ProviderAvailable, ProviderSkipReason);

        var taken = UniquePhone();
        await Repository.AddAsync(User.Register(taken, "Holder", At), default);

        var other = await AddOneAsync("Mover");
        other.SetPhone(taken, hasLinkedProvider: false);

        await Assert.ThrowsAsync<DuplicatePhoneException>(
            () => Repository.SaveAsync(other, default));
    }

    [SkippableFact]
    public async Task Two_accounts_with_no_email_can_both_exist()
    {
        /*
         * <b>The sparse-versus-partial trap, pinned.</b> Registration creates
         * accounts with no address, and a *sparse* unique index only skips
         * documents where the field is MISSING — not where it is present and
         * null. One dropped `[BsonIgnoreIfNull]` and the mapper writes
         * `email: null`, at which point the second address-less account in the
         * system collides with the first and registration stops working for
         * everyone. The index is declared partial on `{ email: {$type:
         * "string"} }` so it survives that; this test is what notices if
         * either guard is removed.
         */
        Skip.IfNot(ProviderAvailable, ProviderSkipReason);

        await Repository.AddAsync(User.Register(UniquePhone(), "No address one", At), default);
        await Repository.AddAsync(User.Register(UniquePhone(), "No address two", At), default);
    }

    [SkippableFact]
    public async Task Two_accounts_with_no_phone_can_both_exist()
    {
        // The mirror case: accounts created through a social provider carry an
        // address and no number.
        Skip.IfNot(ProviderAvailable, ProviderSkipReason);

        await Repository.AddAsync(
            User.RegisterFromProvider(UniqueEmail(), "No number one", At), default);
        await Repository.AddAsync(
            User.RegisterFromProvider(UniqueEmail(), "No number two", At), default);
    }

    [SkippableFact]
    public async Task Saving_updates_the_existing_account_instead_of_adding_another()
    {
        Skip.IfNot(ProviderAvailable, ProviderSkipReason);

        var user = await AddOneAsync("Before");
        user.Rename("After");
        await Repository.SaveAsync(user, default);

        var found = await Repository.FindByIdAsync(user.Id, default);
        Assert.NotNull(found);
        Assert.Equal("After", found.DisplayName);

        // Still exactly one account on that address — a save implemented as an
        // insert would leave two, and every later lookup would return an
        // arbitrary one of them.
        var (matches, total) = await Repository.ListAsync(
            user.Email!.Value.Value, skip: 0, take: 10, default);
        Assert.Equal(1, total);
        Assert.Single(matches);
    }

    [SkippableFact]
    public async Task A_saved_change_survives_a_round_trip_field_by_field()
    {
        // Guards the mapper, not the store: a field dropped in ToDocument or
        // ToDomain reads back as a default, which looks like data loss long
        // after the write that caused it.
        Skip.IfNot(ProviderAvailable, ProviderSkipReason);

        var user = await AddOneAsync("Round Trip");
        user.SetPhone(UniquePhone(), hasLinkedProvider: false);
        user.Suspend();
        await Repository.SaveAsync(user, default);

        var found = await Repository.FindByIdAsync(user.Id, default);

        Assert.NotNull(found);
        Assert.Equal(user.Email, found.Email);
        Assert.Equal(user.Phone, found.Phone);
        Assert.Equal(UserStatus.Suspended, found.Status);
        Assert.False(found.CanAuthenticate);
        Assert.Equal(user.CreatedAt, found.CreatedAt);
    }

    [SkippableFact]
    public async Task A_timestamp_comes_back_as_the_same_instant_in_UTC()
    {
        // <b>The bug this exists to catch is silent and enormous.</b> A
        // DateTime read from storage arrives with Kind = Unspecified, and
        // converting that to a DateTimeOffset applies the SERVER's local
        // offset — on a machine at UTC+7 every stored instant shifts seven
        // hours. Nothing errors; the exam deadlines are simply wrong.
        Skip.IfNot(ProviderAvailable, ProviderSkipReason);

        var user = await AddOneAsync();

        var found = await Repository.FindByIdAsync(user.Id, default);

        Assert.NotNull(found);
        Assert.Equal(At.ToUnixTimeMilliseconds(), found.CreatedAt.ToUnixTimeMilliseconds());
        Assert.Equal(TimeSpan.Zero, found.CreatedAt.Offset);
    }

    [SkippableFact]
    public async Task Listing_bounds_its_result_by_take_and_reports_the_true_total()
    {
        // `total` is what the CMS renders its pager from, so it has to count
        // every match rather than the page — a provider returning the page
        // length would show "1 page" over any number of accounts.
        Skip.IfNot(ProviderAvailable, ProviderSkipReason);

        var shared = $"Cohort{Guid.NewGuid():n}";
        for (var i = 0; i < 3; i++) await AddOneAsync(shared);

        var (page, total) = await Repository.ListAsync(shared, skip: 0, take: 2, default);

        Assert.Equal(2, page.Count);
        Assert.Equal(3, total);
    }

    [SkippableFact]
    public async Task Listing_pages_without_repeating_or_dropping_an_account()
    {
        Skip.IfNot(ProviderAvailable, ProviderSkipReason);

        var shared = $"Cohort{Guid.NewGuid():n}";
        for (var i = 0; i < 3; i++) await AddOneAsync(shared);

        var (first, _) = await Repository.ListAsync(shared, skip: 0, take: 2, default);
        var (second, _) = await Repository.ListAsync(shared, skip: 2, take: 2, default);

        var ids = first.Concat(second).Select(u => u.Id).ToArray();

        Assert.Equal(3, ids.Length);
        Assert.Equal(3, ids.Distinct().Count());
    }

    [SkippableFact]
    public async Task Search_matches_a_display_name_and_excludes_everyone_else()
    {
        Skip.IfNot(ProviderAvailable, ProviderSkipReason);

        var wanted = $"Wanted{Guid.NewGuid():n}";
        var user = await AddOneAsync(wanted);
        await AddOneAsync($"Other{Guid.NewGuid():n}");

        var (matches, total) = await Repository.ListAsync(wanted, skip: 0, take: 10, default);

        Assert.Equal(1, total);
        Assert.Equal(user.Id, Assert.Single(matches).Id);
    }

    [SkippableFact]
    public async Task Search_treats_its_input_as_text_rather_than_as_a_pattern()
    {
        // A search box that reaches the store as a pattern is both a hang risk
        // (`(a+)+$`) and a correctness bug — a caller typing `.*` would match
        // every account in the product. The promise is that a search term
        // means itself.
        Skip.IfNot(ProviderAvailable, ProviderSkipReason);

        await AddOneAsync($"Literal{Guid.NewGuid():n}");

        var (matches, total) = await Repository.ListAsync(".*", skip: 0, take: 10, default);

        Assert.Equal(0, total);
        Assert.Empty(matches);
    }
}
