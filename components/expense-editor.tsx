'use client';

import { useId } from 'react';
import { Input } from '@/components/ui/input';

type ExpenseRow = Record<string, string | null>;

export function ExpenseEditor({ rows, calculatedRows, disabled, onChange, errors = {}, path, onEdit, label = 'Expenses' }: {
	rows: ExpenseRow[]; disabled: boolean; onChange: (rows: ExpenseRow[]) => void;
	calculatedRows?: ExpenseRow[];
	errors?: Record<string, string>; path?: string; onEdit?: (path: string) => void; label?: string;
}) {
	const id = useId();
	return <div className="overflow-x-auto rounded-md border border-slate-200">
		<table aria-label={label} className="w-full min-w-[280px] table-fixed text-xs">
			<colgroup><col /><col className="w-20 sm:w-24" /><col className="w-28 sm:w-32" /></colgroup>
			<thead className="border-b border-slate-200 bg-slate-50 text-[10px] font-medium text-slate-500">
				<tr><th scope="col" className="px-3 py-2 text-left">Expense</th>
					<th scope="col" className="px-2 py-2 text-right">Percent / value</th>
					<th scope="col" className="px-2 py-2 text-right">Amount</th></tr>
			</thead>
			<tbody>{rows.map((row, index) => <tr key={row.SNo} className="border-b border-slate-100 last:border-b-0 hover:bg-slate-50/50">
				<th scope="row" className="break-words px-3 py-2 text-left align-top font-medium text-slate-700">
					{row.Label ?? row.ExpShortCode}
				</th>
				{(['Per', 'Amount'] as const).map(key => {
					const validationPath = path ? `${path}[${index}].${key}` : undefined;
					const error = validationPath ? errors[validationPath] : undefined;
					const editable = row[key === 'Per' ? 'PerEditable' : 'AmtEditable'] === 'Y';
					const calculated = key === 'Amount' ? calculatedRows?.find(candidate => candidate.SNo === row.SNo)?.Amount : undefined;
					return <td key={key} className="px-2 py-1 align-top">
						<Input aria-label={`${row.Label ?? row.ExpShortCode} ${key}`} inputMode="decimal"
							aria-invalid={Boolean(error)} data-validation-path={validationPath}
							aria-describedby={error ? `${id}-${index}-${key}` : undefined}
							className="h-7 rounded-sm border-slate-200 bg-white px-2 text-right text-xs tabular-nums shadow-none disabled:bg-slate-50 disabled:text-slate-600 disabled:opacity-100 md:text-xs"
							value={(!editable ? calculated : undefined) ?? row[key] ?? '0'} disabled={disabled || !editable}
							onChange={event => {
								if (validationPath) onEdit?.(validationPath);
								onChange(rows.map((item, position) => position === index ? { ...item, [key]: event.target.value } : item));
							}} />
						{error && <p id={`${id}-${index}-${key}`} className="mt-1 break-words text-[10px] text-red-700">{error}</p>}
						{editable && calculated != null && calculated !== row[key] &&
							<p className="text-right text-[10px] text-slate-600">Calculated: {calculated}</p>}
					</td>;
				})}
			</tr>)}</tbody>
		</table>
		{!rows.length && <p className="px-3 py-4 text-xs text-slate-500">No expenses configured.</p>}
	</div>;
}
