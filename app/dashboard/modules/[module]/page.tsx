'use client';

import Link from 'next/link';
import { useSearchParams } from 'next/navigation';
import { Construction, FileText } from 'lucide-react';
import { useNavigation } from '@/components/erp-navigation';

export default function ModulePage() {
	const { current } = useNavigation();
	const params = useSearchParams();
	const key = params.get('menu');
	const selected = current?.items.find((item) => item.key === key);
	if (!current || key && !selected) return <p role="alert">This module or menu is not permitted in your current context.</p>;
	return <section className="space-y-5">
		<div><p className="text-xs text-slate-400 mb-1">{current.label} module</p>
			<h1 className="text-xl font-semibold text-slate-900">{selected?.label ?? current.label}</h1></div>
		{selected && !selected.implemented && <div className="rounded-lg border border-amber-200 bg-amber-50 p-5 flex gap-3">
			<Construction className="size-5 shrink-0 text-amber-600" /><div>
				<h2 className="font-semibold text-amber-900">Not yet available in web POC</h2>
				<p className="text-sm text-amber-800 mt-1">This authorized desktop menu is shown for navigation review. Its transaction screen has not been migrated; no business operation has been executed.</p>
			</div>
		</div>}
		<p className="text-sm text-slate-500">Choose a menu on the left, or use these module shortcuts.</p>
		<div className="grid gap-2 sm:grid-cols-2 xl:grid-cols-3">
			{current.items.filter((item) => !current.items.some((child) => child.parentKey === item.key)).map((item) =>
				<Link key={item.key} href={item.href} className="rounded-lg border border-slate-200 bg-white p-3 flex items-start gap-3 hover:border-blue-300">
					<FileText className="size-4 text-blue-600 mt-0.5 shrink-0" /><div><p className="font-medium">{item.label}</p>
						<p className={`text-[11px] mt-1 ${item.implemented ? 'text-emerald-700' : 'text-slate-400'}`}>{item.implemented ? 'Available' : 'Not yet available in web POC'}</p></div>
				</Link>)}
		</div>
	</section>;
}
