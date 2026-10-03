export function InstallSteps({ steps }: { steps: readonly string[] }) {
  return (
    <ol className="space-y-2.5">
      {steps.map((step, i) => (
        <li
          key={step}
          className="flex items-center gap-4 rounded-3xl bg-surface py-2.5 pr-5 pl-2.5 text-left text-sm sm:rounded-full sm:text-base"
        >
          <span className="flex size-8 shrink-0 items-center justify-center rounded-full bg-ink text-sm font-medium text-paper">
            {i + 1}
          </span>
          <span className="text-muted">{step}</span>
        </li>
      ))}
    </ol>
  );
}
