/** Validate select/form strings before they cross the typed API boundary. */
export function enumValue<const T extends string>(choices: readonly T[], value: string): T {
  const match = choices.find((choice) => choice === value);
  if (match === undefined) throw new Error(`Invalid selection: ${value}`);
  return match;
}
