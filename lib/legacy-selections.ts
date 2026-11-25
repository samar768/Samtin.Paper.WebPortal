export function parseLegacySelection(value: string | null): { keys: string[]; error?: string } {
	if (!value) return { keys: [] };
	if (!/^\|[^|,]+\|(?:,\s*\|[^|,]+\|)*$/.test(value))
		return { keys: [], error: 'The stored selection has an unsupported format. It has not been changed; use the lookup to replace it.' };
	const keys = [...value.matchAll(/\|([^|,]+)\|/g)].map(match => match[1]);
	if (keys.length > 100 || new Set(keys).size !== keys.length)
		return { keys: [], error: 'The stored selection must contain at most 100 distinct codes. It has not been changed.' };
	return { keys };
}

export function serializeLegacySelection(keys: string[]) {
	if (keys.length > 100 || new Set(keys).size !== keys.length || keys.some(key => !key || /[|,]/.test(key)))
		throw new Error('Select at most 100 distinct codes without reserved separators.');
	return keys.map(key => `|${key}|`).join(',');
}

export function updateLegacySelection(selected: string[], keys: string[], checked: boolean): string[] {
	const affected = new Set(keys);
	return checked ? [...new Set([...selected, ...keys])] : selected.filter(key => !affected.has(key));
}

export function applyLegacySelection(value: string | null, keys: string[]): string | null {
	const serialized = serializeLegacySelection(keys);
	const previous = parseLegacySelection(value);
	if (!previous.error && previous.keys.length === keys.length && keys.every(key => previous.keys.includes(key)))
		return value;
	return serialized;
}
