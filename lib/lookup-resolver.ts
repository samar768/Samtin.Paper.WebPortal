type Reference = { key: string };

export function createLookupResolver<T extends Reference>(
	load: (scope: string, lookup: string, keys: string[]) => Promise<T[]>, ttl = 30000,
) {
	type Waiter = { key: string; resolve: (value: T | null) => void; reject: (error: unknown) => void };
	const cache = new Map<string, { value: T | null; expires: number }>();
	const pending = new Map<string, Waiter[]>();
	const revisions = new Map<string, number>();
	const listeners = new Set<() => void>();
	let generation = 0;
	const groupKey = (scope: string, lookup: string) => JSON.stringify([scope, lookup]);
	const cacheKey = (scope: string, lookup: string, key: string) => JSON.stringify([scope, lookup, key]);
	async function flush(scope: string, lookup: string, group: string) {
		const waiters = pending.get(group) ?? [];
		pending.delete(group);
		const keys = [...new Set(waiters.map(waiter => waiter.key))];
		const requestGeneration = generation;
		const requestRevision = revisions.get(group) ?? 0;
		for (let start = 0; start < keys.length; start += 100) {
			const chunk = keys.slice(start, start + 100);
			const recipients = waiters.filter(waiter => chunk.includes(waiter.key));
			try {
				const options = await load(scope, lookup, chunk);
				if (options.some(option => !chunk.includes(option.key)) || new Set(options.map(option => option.key)).size !== options.length)
					throw new Error('Lookup resolution returned unexpected reference keys.');
				const found = new Map(options.map(option => [option.key, option]));
				for (const key of chunk) {
					if (generation !== requestGeneration || (revisions.get(group) ?? 0) !== requestRevision) continue;
					for (const [oldKey, item] of cache) if (item.expires <= Date.now()) cache.delete(oldKey);
					if (cache.size >= 500) {
						const oldest = cache.keys().next().value;
						if (oldest !== undefined) cache.delete(oldest);
					}
					cache.set(cacheKey(scope, lookup, key), { value: found.get(key) ?? null, expires: Date.now() + ttl });
				}
				for (const waiter of recipients) waiter.resolve(found.get(waiter.key) ?? null);
			} catch (error) {
				for (const waiter of recipients) waiter.reject(error);
			}
		}
	}
	return {
		resolve(scope: string, lookup: string, key: string): Promise<T | null> {
			const cached = cache.get(cacheKey(scope, lookup, key));
			if (cached && cached.expires > Date.now()) return Promise.resolve(cached.value);
			return new Promise((resolve, reject) => {
				const group = groupKey(scope, lookup);
				const waiters = pending.get(group);
				if (waiters) waiters.push({ key, resolve, reject });
				else {
					pending.set(group, [{ key, resolve, reject }]);
					setTimeout(() => { void flush(scope, lookup, group); }, 0);
				}
			});
		},
		subscribe(listener: () => void) { listeners.add(listener); return () => { listeners.delete(listener); }; },
		version(scope: string, lookup: string) { return generation + (revisions.get(groupKey(scope, lookup)) ?? 0); },
		invalidate(scope: string, lookup: string) {
			const group = groupKey(scope, lookup);
			revisions.set(group, (revisions.get(group) ?? 0) + 1);
			for (const key of cache.keys()) {
				const [cachedScope, cachedLookup] = JSON.parse(key) as string[];
				if (cachedScope === scope && cachedLookup === lookup) cache.delete(key);
			}
			for (const listener of listeners) listener();
		},
		clear() { generation++; cache.clear(); for (const listener of listeners) listener(); },
	};
}
