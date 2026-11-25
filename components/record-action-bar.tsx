'use client';

import { Fragment, type ReactNode } from 'react';
import { ChevronDown, Info, MoreHorizontal, type LucideIcon } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuSeparator, DropdownMenuTrigger } from '@/components/ui/dropdown-menu';
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover';

export type RecordAction = {
	id: string; label: string; icon: LucideIcon; onSelect: () => void;
	group?: string; disabled?: boolean; reason?: string; destructive?: boolean;
};
export type RecordDetail = { label: string; value: string | null | undefined };

export function RecordActionBar({ actions, primary, details = [], status, busy, leading }: {
	actions: RecordAction[]; primary?: RecordAction; details?: RecordDetail[]; status: string; busy: boolean;
	leading?: ReactNode;
}) {
	return <footer role="region" aria-label="Record actions" className="sticky bottom-0 z-30 flex flex-wrap items-center justify-between gap-2 rounded-xl border border-slate-200 bg-white/95 p-3 shadow-[0_-4px_18px_-8px_#33415540] backdrop-blur">
		<div className="flex min-w-0 flex-wrap items-center gap-2">
			<span role="status" className="text-[11px] text-slate-500">{busy ? 'Working...' : status}</span>
			{details.length > 0 && <Popover><PopoverTrigger asChild>
				<Button size="sm" variant="ghost" className="h-8 text-xs"><Info className="size-3.5" />Record details</Button>
			</PopoverTrigger><PopoverContent align="start" side="top" className="w-72 max-w-[calc(100vw-2rem)]">
				<h2 className="mb-3 text-sm font-semibold">Record details</h2>
				<dl className="space-y-2 text-xs">{details.map(detail => <div key={detail.label} className="flex justify-between gap-4">
					<dt className="text-slate-500">{detail.label}</dt><dd className="break-all text-right font-medium">{detail.value || 'Not recorded'}</dd>
				</div>)}</dl>
			</PopoverContent></Popover>}
			{leading}
		</div>
		<div className="ml-auto flex items-center gap-2">
			{actions.length > 0 && <DropdownMenu><DropdownMenuTrigger asChild>
				<Button variant="outline" size="sm" className="h-9 border-blue-200 text-xs text-blue-700 hover:bg-blue-50" disabled={busy}>
					<MoreHorizontal className="size-4" />Options<ChevronDown className="size-3" />
				</Button>
			</DropdownMenuTrigger><DropdownMenuContent align="end" side="top" className="min-w-52">
				{actions.map((action, index) => <Fragment key={action.id}>
					{index > 0 && action.group !== actions[index - 1].group && <DropdownMenuSeparator />}
					<DropdownMenuItem disabled={busy || action.disabled} title={action.reason} variant={action.destructive ? 'destructive' : 'default'}
						onSelect={() => action.onSelect()} className="gap-3 py-2 text-xs"><action.icon className="size-4" />{action.label}</DropdownMenuItem>
				</Fragment>)}
			</DropdownMenuContent></DropdownMenu>}
			{primary && <Button size="sm" className="h-9 bg-blue-600 text-xs hover:bg-blue-700" disabled={busy || primary.disabled}
				onClick={primary.onSelect}><primary.icon className="size-3.5" />{primary.label}</Button>}
		</div>
	</footer>;
}
