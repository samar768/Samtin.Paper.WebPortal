export type FieldErrors = Record<string, string>;

export function bindFieldErrors(errors: Record<string, string[]>, itemKeys: string[]): FieldErrors {
	const bound: FieldErrors = {};
	for (const [path, messages] of Object.entries(errors)) {
		const line = /^(lines|lineExpenses)\[(\d+)\](.+)$/.exec(path);
		const key = line ? itemKeys[Number(line[2])] : undefined;
		if (line && key === undefined) throw new Error('The server returned an unknown validation row. Reload the document.');
		const target = line ? `items[${key}]${line[1] === 'lineExpenses' ? '.expenses' : ''}${line[3]}` : path;
		if (messages.length) bound[target] = messages.join(' ');
	}
	return bound;
}

export function clearFieldError(errors: FieldErrors, path: string): FieldErrors {
	return Object.fromEntries(Object.entries(errors).filter(([key]) => key !== path));
}
