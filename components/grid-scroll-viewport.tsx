'use client';

import { useEffect, useRef, useState, type ReactNode, type RefObject } from 'react';
import { cn } from '@/lib/utils';

export function GridScrollViewport({ children, label, className, viewportRef }: {
	children: ReactNode; label: string; className?: string; viewportRef?: RefObject<HTMLDivElement | null>;
}) {
	const localRef = useRef<HTMLDivElement | null>(null);
	const elementRef = viewportRef ?? localRef;
	const contentRef = useRef<HTMLDivElement | null>(null);
	const [edges, setEdges] = useState({ left: false, right: false });
	const [scrolling, setScrolling] = useState(false);

	useEffect(() => {
		const viewport = elementRef.current;
		const content = contentRef.current;
		if (!viewport || !content) return;
		let timer: ReturnType<typeof setTimeout> | undefined;
		const measure = () => {
			const left = viewport.scrollLeft > 1;
			const right = viewport.scrollWidth - viewport.clientWidth - viewport.scrollLeft > 1;
			setEdges(previous => previous.left === left && previous.right === right ? previous : { left, right });
		};
		const scroll = () => {
			measure(); setScrolling(true);
			clearTimeout(timer);
			timer = setTimeout(() => setScrolling(false), 800);
		};
		const observer = new ResizeObserver(measure);
		observer.observe(viewport);
		observer.observe(content);
		if (content.firstElementChild) observer.observe(content.firstElementChild);
		const frame = requestAnimationFrame(measure);
		viewport.addEventListener('scroll', scroll, { passive: true });
		return () => {
			observer.disconnect(); cancelAnimationFrame(frame); clearTimeout(timer);
			viewport.removeEventListener('scroll', scroll);
		};
	}, [elementRef]);

	return <div className="relative min-w-0" data-scroll-left={edges.left} data-scroll-right={edges.right}>
		<div ref={elementRef} role="region" aria-label={label} tabIndex={0} data-scrolling={scrolling}
			className={cn('erp-grid-scroll overflow-auto focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-blue-500', className)}>
			<div ref={contentRef}>{children}</div>
		</div>
		{edges.left && <div aria-hidden="true" className="pointer-events-none absolute bottom-2 left-0 top-0 z-25 w-3 bg-gradient-to-r from-slate-500/20 to-transparent" />}
		{edges.right && <div aria-hidden="true" className="pointer-events-none absolute bottom-2 right-0 top-0 z-25 w-3 bg-gradient-to-l from-slate-500/20 to-transparent" />}
	</div>;
}
