const mode = process.argv[2];
if (mode === 'zero') process.exit(0);
if (mode === 'nonzero') process.exit(23);
throw new Error(`unknown fixture probe mode: ${mode}`);
