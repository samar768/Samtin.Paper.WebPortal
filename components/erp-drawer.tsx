'use client';

import { X } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from '@/components/ui/dialog';

export function ErpDrawer({ title, description, busy, onClose, children, onCloseAutoFocus }: {
	title: string; description: string; busy: boolean; onClose: () => void; children: React.ReactNode;
	onCloseAutoFocus?: (event: Event) => void;
}) {
	return <Dialog open onOpenChange={open => { if (!open && !busy) onClose(); }}>
		<DialogContent showCloseButton={false} onCloseAutoFocus={onCloseAutoFocus}
			onInteractOutside={event => event.preventDefault()}
			onEscapeKeyDown={event => { if (busy) event.preventDefault(); }}
			className="left-auto right-0 top-0 flex h-dvh w-full max-w-full translate-x-0 translate-y-0 flex-col gap-0 rounded-none border-y-0 border-r-0 p-0 sm:w-[480px] sm:max-w-[calc(100vw-2rem)] data-[state=open]:slide-in-from-right data-[state=closed]:slide-out-to-right data-[state=open]:zoom-in-100 data-[state=closed]:zoom-out-100">
			<DialogHeader className="shrink-0 border-b bg-slate-50 px-5 py-5 pr-14 text-left">
				<DialogTitle className="text-base">{title}</DialogTitle>
				<DialogDescription className="text-xs">{description}</DialogDescription>
			</DialogHeader>
			<Button type="button" variant="ghost" size="icon" aria-label="Close master form" disabled={busy}
				className="absolute right-3 top-3 size-8" onClick={onClose}><X className="size-4" /></Button>
			{children}
		</DialogContent>
	</Dialog>;
}
