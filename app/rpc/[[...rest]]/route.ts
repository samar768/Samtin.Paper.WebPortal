function retiredLookupAdapter() {
	return Response.json(
		{ detail: 'The prototype lookup adapter is retired. Use the authenticated POC lookup API.' },
		{ status: 410 },
	);
}

export const GET = retiredLookupAdapter;
export const POST = retiredLookupAdapter;
