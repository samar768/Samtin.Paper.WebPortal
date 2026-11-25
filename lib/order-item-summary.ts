import type { ItemDraft } from './order-draft';
import type { Field } from './poc-api';

export function isVisibleOrderField(field: Pick<Field, 'section' | 'key'>): boolean {
	return !(field.section === 'header' && field.key.toLowerCase() === 'webpocreference' ||
		field.section === 'lines' && field.key.toLowerCase() === 'webpocmeasure');
}

function total(items: ItemDraft[], key: string): string | null {
	let hundredths = BigInt(0);
	for (const item of items) {
		if (item.entryOnly) continue;
		const text = item.values[key]?.trim() || '0';
		if (!/^\+?(?:\d+\.?\d*|\.\d+)(?:e[+-]?\d+)?$/i.test(text)) return null;
		const [mantissa, exponent = '0'] = text.toLowerCase().split('e');
		const power = Number(exponent);
		if (!Number.isSafeInteger(power) || Math.abs(power) > 324) return null;
		const shift = power + 2 - (mantissa.split('.')[1]?.length ?? 0);
		const digits = BigInt(mantissa.replace(/[+.]/g, ''));
		const factor = BigInt(10) ** BigInt(Math.abs(shift));
		// Windows formats each running quantity/weight total to two decimal places.
		hundredths += shift >= 0 ? digits * factor : (digits + factor / BigInt(2)) / factor;
	}
	return `${hundredths / BigInt(100)}.${(hundredths % BigInt(100)).toString().padStart(2, '0')}`;
}

export function summarizeOrderItems(items: ItemDraft[]) {
	return { quantity: total(items, 'Reel'), weight: total(items, 'Qty') };
}
