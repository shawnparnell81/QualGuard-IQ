import { useState } from 'react';
import { flexRender, getCoreRowModel, getSortedRowModel, useReactTable, type ColumnDef, type SortingState } from '@tanstack/react-table';
import { Paper, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, TableSortLabel, Typography } from '@mui/material';

export function DataTable<T>({ data, columns, onRowClick, empty = 'Nothing to show.' }: {
  data: T[]; columns: ColumnDef<T, any>[]; onRowClick?: (row: T) => void; empty?: string;
}) {
  const [sorting, setSorting] = useState<SortingState>([]);
  const table = useReactTable({ data, columns, state: { sorting }, onSortingChange: setSorting, getCoreRowModel: getCoreRowModel(), getSortedRowModel: getSortedRowModel() });
  return (
    <TableContainer component={Paper}>
      <Table size="small">
        <TableHead>
          {table.getHeaderGroups().map((hg) => (
            <TableRow key={hg.id}>
              {hg.headers.map((h) => (
                <TableCell key={h.id}>
                  {h.column.getCanSort() ? (
                    <TableSortLabel active={!!h.column.getIsSorted()} direction={h.column.getIsSorted() === 'desc' ? 'desc' : 'asc'} onClick={h.column.getToggleSortingHandler()}>
                      {flexRender(h.column.columnDef.header, h.getContext())}
                    </TableSortLabel>
                  ) : flexRender(h.column.columnDef.header, h.getContext())}
                </TableCell>
              ))}
            </TableRow>
          ))}
        </TableHead>
        <TableBody>
          {table.getRowModel().rows.map((r) => (
            <TableRow key={r.id} hover={!!onRowClick} onClick={() => onRowClick?.(r.original)} sx={{ cursor: onRowClick ? 'pointer' : undefined }}>
              {r.getVisibleCells().map((c) => <TableCell key={c.id}>{flexRender(c.column.columnDef.cell, c.getContext())}</TableCell>)}
            </TableRow>
          ))}
          {data.length === 0 && (
            <TableRow><TableCell colSpan={columns.length}><Typography color="text.secondary" sx={{ py: 3, textAlign: 'center' }}>{empty}</Typography></TableCell></TableRow>
          )}
        </TableBody>
      </Table>
    </TableContainer>
  );
}
