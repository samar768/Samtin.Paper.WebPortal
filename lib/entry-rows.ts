export type EntryRow = { key: string; entryOnly?: boolean };

export function ensureEntryRow<T extends EntryRow>(rows: T[], create: () => T, limit: number): T[] {
	const active = rows.filter((row) => !row.entryOnly);
	if (active.length >= limit) return active;
	const entry = rows.find((row) => row.entryOnly);
	return [...active, entry ?? { ...create(), entryOnly: true }];
}

export function editEntryRow<T extends EntryRow>(
	rows: T[], key: string, update: (row: T) => T, create: () => T, limit: number,
): T[] {
	if (!rows.some((row) => row.key === key)) throw new Error('This entry row no longer exists. Reload the document.');
	return ensureEntryRow(rows.map((row) => row.key === key ? { ...update(row), entryOnly: false } : row), create, limit);
}
