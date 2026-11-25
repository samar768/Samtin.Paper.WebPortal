'use client';

import Link from 'next/link';
import { useState } from 'react';
import { Building2 } from 'lucide-react';
import { useSession } from '@/components/poc-session';
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from '@/components/ui/dialog';

export function CompanyInformation() {
	const session = useSession();
	const info = session.companyInformation;
	const [open, setOpen] = useState(false);
	const address = [info.address1, info.address2, info.city, info.country, info.pin].filter(Boolean).join(', ');
	return <>
		<button className="flex min-w-0 items-center gap-2 rounded-md p-1 text-right hover:bg-slate-50 focus-visible:outline-2 focus-visible:outline-blue-500"
			aria-label="Company information" title={`${session.companyName} · ${session.financialYear} · ${session.siteName}`} onClick={() => setOpen(true)}>
			<Building2 className="size-4 shrink-0 text-slate-500" />
			<span className="hidden lg:block text-xs"><span className="block max-w-56 truncate font-medium">{session.companyName}</span>
				<span className="block text-slate-400">FY {session.financialYear} · {session.site}{session.readOnly ? ' · Read-only' : ''}</span></span>
		</button>
		<Dialog open={open} onOpenChange={setOpen}><DialogContent className="max-h-[85dvh] overflow-y-auto sm:max-w-xl">
			<DialogHeader><DialogTitle>Company information</DialogTitle><DialogDescription className="text-xs">Current server-validated workspace. All modules use this company, year and site.</DialogDescription></DialogHeader>
			<dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-3 text-xs [&_dd]:min-w-0 [&_dd]:break-words [&_dt]:text-slate-500">
				<dt>Company</dt><dd>{info.registeredName} ({session.company})</dd>
				{info.displayName !== info.registeredName && <><dt>Site trading name</dt><dd>{info.displayName}</dd></>}
				<dt>Financial year</dt><dd>{session.financialYear}<span className="block text-slate-500">{session.startDate} – {session.endDate}</span></dd>
				<dt>Site</dt><dd>{session.siteName} ({session.site}){info.headOffice ? ' · Head Office' : ''}</dd>
				<dt>Mode</dt><dd>{session.readOnly ? 'Read-only in this POC' : 'Subject to your ERP permissions and record locks'}</dd>
				<dt>Address</dt><dd>{address || 'Not recorded'}</dd>
				<dt>Phone</dt><dd>{info.phone || 'Not recorded'}</dd>
				<dt>Email</dt><dd>{info.email || 'Not recorded'}</dd>
				<dt>PAN</dt><dd>{info.pan || 'Not recorded'}</dd>
				<dt>TIN</dt><dd>{info.tin || 'Not recorded'}</dd>
			</dl>
			<Link href="/select-company" className="text-xs font-medium text-blue-700 hover:underline">Change company / financial year / site</Link>
		</DialogContent></Dialog>
	</>;
}
