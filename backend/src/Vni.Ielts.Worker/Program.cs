using Vni.Ielts.Infrastructure;
using Vni.Ielts.Worker;

/*
 * <b>The worker is a real process now, and it was a template.</b>
 *
 * What stood here registered a `BackgroundService` that logged the time once a
 * second against no dependencies at all — so the marking queue had nothing
 * draining it, and the process looked healthy while doing nothing. Worse than
 * an empty file, because a running service is evidence to whoever checks.
 *
 * <b>`AddInfrastructure`, which is the whole point.</b> The worker needs the
 * same stores, the same evaluator ports and the same rubric source the API
 * uses; a second, worker-shaped composition root would be a second definition
 * of what marking means, and the one that drifts is always the one nobody has
 * filed a bug against.
 */
var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration, builder.Environment.IsDevelopment());
builder.Services.AddHostedService<MarkingWorker>();

/*
 * <b>Off unless switched on</b> — see the class. A background process that
 * deletes audio is not something to enable by default in an environment nobody
 * has looked at, and the moment somebody enables it is the moment they decide
 * what the retention window is. → `I2.5`
 */
builder.Services.AddHostedService<ReconciliationWorker>();

var host = builder.Build();

/*
 * <b>Indexes, then work.</b> The worker's claim is a filtered update over
 * state and due time, and without the index behind it every poll is a
 * collection scan. It shares the initialisation the API runs for the same
 * reason it shares the composition root: two of them would drift.
 */
await host.Services.InitialiseInfrastructureAsync(CancellationToken.None);

await host.RunAsync();
