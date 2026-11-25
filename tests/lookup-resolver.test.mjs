import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createLookupResolver } from '../lib/lookup-resolver.ts';

test('description resolution batches duplicate keys and reuses cached descriptions', async () => {
	const calls = [];
	const resolver = createLookupResolver(async (scope, lookup, keys) => {
		calls.push({ scope, lookup, keys });
		return keys.map(key => ({ key, label: 'Same description' }));
	});
	const [one, again, two] = await Promise.all([
		resolver.resolve('workspace', 'parties', 'K1'), resolver.resolve('workspace', 'parties', 'K1'),
		resolver.resolve('workspace', 'parties', 'K2'),
	]);
	assert.equal(calls.length, 1);
	assert.deepEqual(calls[0].keys, ['K1', 'K2']);
	assert.equal(one, again);
	assert.equal(one.label, two.label);
	assert.notEqual(one.key, two.key);
	assert.equal((await resolver.resolve('workspace', 'parties', 'K1')).key, 'K1');
	assert.equal(calls.length, 1);
});

test('cached labels cannot cross workspace or lookup boundaries', async () => {
	const calls = [];
	const resolver = createLookupResolver(async (scope, lookup, keys) => {
		calls.push([scope, lookup]);
		return keys.map(key => ({ key, label: `${scope}:${lookup}` }));
	});
	const values = await Promise.all([
		resolver.resolve('HO', 'parties', 'K1'), resolver.resolve('D1', 'parties', 'K1'),
		resolver.resolve('HO', 'items', 'K1'),
	]);
	assert.deepEqual(values.map(value => value.label), ['HO:parties', 'D1:parties', 'HO:items']);
	assert.equal(calls.length, 3);
});

test('master invalidation refreshes its labels and missing keys without clearing other lookups or workspaces', async () => {
		const calls = [];
		let available = false;
		const resolver = createLookupResolver(async (scope, lookup, keys) => {
			calls.push([scope, lookup]);
			return available ? keys.map(key => ({ key, label: 'Saved master' })) : [];
		});
		await Promise.all([resolver.resolve('HO', 'bf', 'HO42'), resolver.resolve('HO', 'gsm', 'HO42'), resolver.resolve('D1', 'bf', 'HO42')]);
		available = true;
		let notifications = 0;
		const unsubscribe = resolver.subscribe(() => notifications++);
		const before = resolver.version('HO', 'bf');
		resolver.invalidate('HO', 'bf');
		assert.equal(resolver.version('HO', 'bf'), before + 1);
		assert.equal(resolver.version('HO', 'gsm'), 0);
		assert.equal((await resolver.resolve('HO', 'bf', 'HO42')).label, 'Saved master');
		assert.equal(await resolver.resolve('HO', 'gsm', 'HO42'), null);
		assert.equal(await resolver.resolve('D1', 'bf', 'HO42'), null);
		assert.equal(calls.length, 4);
		assert.equal(notifications, 1);
		unsubscribe();
});

test('an in-flight response cannot recache an old name after a scoped master update', async () => {
		let completeOld;
		let count = 0;
		const resolver = createLookupResolver(async () => {
			if (++count === 1) return new Promise(resolve => { completeOld = resolve; });
			return [{ key: 'HO42', label: 'Edited name' }];
		});
		const old = resolver.resolve('HO', 'bf', 'HO42');
		await new Promise(resolve => setTimeout(resolve, 10));
		resolver.invalidate('HO', 'bf');
		assert.equal((await resolver.resolve('HO', 'bf', 'HO42')).label, 'Edited name');
		completeOld([{ key: 'HO42', label: 'Old name' }]);
		await old;
		assert.equal((await resolver.resolve('HO', 'bf', 'HO42')).label, 'Edited name');
});

test('missing keys are explicit, failed requests are not cached and Retry can refresh names', async () => {
	let attempt = 0;
	const resolver = createLookupResolver(async (_scope, _lookup, keys) => {
		if (++attempt === 1) throw new Error('lookup unavailable');
		return keys.filter(key => key !== 'missing').map(key => ({ key, label: `Name ${attempt}` }));
	});
	await assert.rejects(resolver.resolve('HO', 'parties', 'K1'), /unavailable/);
	assert.equal((await resolver.resolve('HO', 'parties', 'K1')).label, 'Name 2');
	assert.equal(await resolver.resolve('HO', 'parties', 'missing'), null);
	resolver.clear();
	assert.equal((await resolver.resolve('HO', 'parties', 'K1')).label, 'Name 4');
});

test('resolution respects the 100-key endpoint bound and rejects unexpected keys', async () => {
	const batches = [];
	const resolver = createLookupResolver(async (_scope, _lookup, keys) => {
		batches.push(keys.length);
		return keys.map(key => ({ key }));
	});

	await Promise.all(Array.from({ length: 125 }, (_, index) => resolver.resolve('HO', 'parties', `K${index}`)));
	assert.deepEqual(batches, [100, 25]);
	const invalid = createLookupResolver(async () => [{ key: 'unrequested' }]);
	await assert.rejects(invalid.resolve('HO', 'parties', 'K1'), /unexpected reference keys/);
});

test('an older in-flight resolution cannot overwrite the cache after Retry', async () => {
	let completeOld;
	let calls = 0;
	const resolver = createLookupResolver(async () => {
		if (++calls === 1) return new Promise(resolve => { completeOld = resolve; });
		return [{ key: 'K1', label: 'New name' }];
	});
	const old = resolver.resolve('HO', 'parties', 'K1');
	await new Promise(resolve => setTimeout(resolve, 10));
	resolver.clear();
	assert.equal((await resolver.resolve('HO', 'parties', 'K1')).label, 'New name');
	completeOld([{ key: 'K1', label: 'Old name' }]);
	await old;
	assert.equal((await resolver.resolve('HO', 'parties', 'K1')).label, 'New name');
});
