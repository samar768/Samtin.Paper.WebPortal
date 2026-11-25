'use client';

import Link from 'next/link';
import { useEffect, useRef } from 'react';
import { X } from 'lucide-react';

type WorkspaceTab = { key: string; label: string; href: string; active: boolean; dirty?: boolean; busy?: boolean; close?: () => void };

function Tab({ tab }: { tab: WorkspaceTab }) {
	return <div className={`flex shrink-0 items-center rounded-md border ${tab.active ? 'border-blue-200 bg-blue-50 text-blue-700' : 'border-transparent text-slate-600 hover:bg-slate-50'}`}>
		<Link href={tab.href} scroll={false} aria-current={tab.active ? 'page' : undefined} title={tab.label}
			className="flex min-h-8 items-center gap-1.5 rounded px-2.5 text-xs font-medium whitespace-nowrap hover:text-blue-700 focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-blue-500">
			<span className="max-w-28 truncate sm:max-w-44">{tab.label}</span>
			{tab.dirty && <span className="size-1.5 rounded-full bg-amber-500" role="img" aria-label="Unsaved changes" />}
		</Link>
		{tab.close && <button type="button" className="mr-1 rounded p-1.5 hover:bg-slate-200 focus-visible:outline-blue-500 disabled:opacity-40"
			disabled={tab.busy} aria-label={`Close ${tab.label}`} onClick={tab.close}><X className="size-3.5" /></button>}
	</div>;
}

export function WorkspaceTabStrip({ label, pinned, tabs }: {
	label: string;
	pinned: WorkspaceTab;
	tabs: WorkspaceTab[];
}) {
	const strip = useRef<HTMLDivElement>(null);
	const activeKey = tabs.find(tab => tab.active)?.key;
	useEffect(() => {
		strip.current?.querySelector('[aria-current="page"]')?.parentElement?.scrollIntoView({ block: 'nearest', inline: 'nearest' });
	}, [activeKey]);

	return <nav aria-label={label} className="flex min-w-0 items-center gap-1 py-1">
		<Tab tab={pinned} />
		{tabs.length > 0 && <div ref={strip} className="flex min-w-0 gap-1 overflow-x-auto border-l border-slate-200 pl-1">
			{tabs.map(tab => <Tab key={tab.key} tab={tab} />)}
		</div>}
	</nav>;
}
