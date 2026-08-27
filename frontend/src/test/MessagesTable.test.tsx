import React from "react";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { MessagesTable } from "../components/MessagesTable";
import * as api from "../lib/api";
import { MessageFilterState, PagedResult, MessageListItem } from "../types/dashboard";

vi.mock("../lib/api", () => ({
  exportStatusLogsCsvApi: vi.fn(),
}));

describe("MessagesTable Component & CSV Export Periods", () => {
  const sampleFilters: MessageFilterState = {
    page: 1,
    pageSize: 20,
    status: "ALL",
    search: "wamid.123",
    dateFrom: "",
    dateTo: "",
  };

  const sampleData: PagedResult<MessageListItem> = {
    items: [],
    totalCount: 0,
    page: 1,
    pageSize: 20,
    totalPages: 1,
  };

  beforeEach(() => {
    vi.clearAllMocks();
    // Mock URL object methods for download triggering
    window.URL.createObjectURL = vi.fn(() => "blob:http://localhost/mock-blob");
    window.URL.revokeObjectURL = vi.fn();
  });

  it("defaults to Last 7 Days and sends rolling UTC date range on export", async () => {
    const mockBlob = new Blob(["csv content"], { type: "text/csv" });
    vi.mocked(api.exportStatusLogsCsvApi).mockResolvedValueOnce({
      blob: mockBlob,
      filename: "whatsapp_status_logs_tenant_20260827.csv",
    });

    render(
      <MessagesTable
        data={sampleData}
        filters={sampleFilters}
        onFilterChange={vi.fn()}
        onSelectMessage={vi.fn()}
        loading={false}
        onRefresh={vi.fn()}
        customTenantHeader="tenant-xyz"
      />
    );

    const exportPeriodSelect = screen.getByLabelText("Export Period") as HTMLSelectElement;
    expect(exportPeriodSelect.value).toBe("7d");

    const downloadBtn = screen.getByRole("button", { name: /download csv/i });
    fireEvent.click(downloadBtn);

    await waitFor(() => {
      expect(api.exportStatusLogsCsvApi).toHaveBeenCalledTimes(1);
    });

    const calledFilters = vi.mocked(api.exportStatusLogsCsvApi).mock.calls[0][0];
    const calledTenant = vi.mocked(api.exportStatusLogsCsvApi).mock.calls[0][1];

    expect(calledTenant).toBe("tenant-xyz");
    expect(calledFilters.status).toBe("ALL");
    expect(calledFilters.search).toBe("wamid.123");
    expect(calledFilters.dateFrom).toBeDefined();
    expect(calledFilters.dateTo).toBeDefined();

    // Verify rolling 7-day difference
    const dFrom = new Date(calledFilters.dateFrom!).getTime();
    const dTo = new Date(calledFilters.dateTo!).getTime();
    const diffDays = Math.round((dTo - dFrom) / (24 * 60 * 60 * 1000));
    expect(diffDays).toBe(7);
  });

  it("sends rolling 30-day and 90-day UTC ranges when selected", async () => {
    const mockBlob = new Blob(["csv content"], { type: "text/csv" });
    vi.mocked(api.exportStatusLogsCsvApi).mockResolvedValue({
      blob: mockBlob,
      filename: "test.csv",
    });

    render(
      <MessagesTable
        data={sampleData}
        filters={sampleFilters}
        onFilterChange={vi.fn()}
        onSelectMessage={vi.fn()}
        loading={false}
        onRefresh={vi.fn()}
      />
    );

    const exportPeriodSelect = screen.getByLabelText("Export Period") as HTMLSelectElement;
    fireEvent.change(exportPeriodSelect, { target: { value: "30d" } });

    const downloadBtn = screen.getByRole("button", { name: /download csv/i });
    fireEvent.click(downloadBtn);

    await waitFor(() => {
      expect(api.exportStatusLogsCsvApi).toHaveBeenCalledTimes(1);
    });

    const calledFilters30 = vi.mocked(api.exportStatusLogsCsvApi).mock.calls[0][0];
    const diff30 = Math.round(
      (new Date(calledFilters30.dateTo!).getTime() - new Date(calledFilters30.dateFrom!).getTime()) /
        (24 * 60 * 60 * 1000)
    );
    expect(diff30).toBe(30);

    // Switch to 90d
    fireEvent.change(exportPeriodSelect, { target: { value: "90d" } });
    fireEvent.click(downloadBtn);

    await waitFor(() => {
      expect(api.exportStatusLogsCsvApi).toHaveBeenCalledTimes(2);
    });

    const calledFilters90 = vi.mocked(api.exportStatusLogsCsvApi).mock.calls[1][0];
    const diff90 = Math.round(
      (new Date(calledFilters90.dateTo!).getTime() - new Date(calledFilters90.dateFrom!).getTime()) /
        (24 * 60 * 60 * 1000)
    );
    expect(diff90).toBe(90);
  });

  it("disables export button when Custom Date Range is selected without valid dates", async () => {
    render(
      <MessagesTable
        data={sampleData}
        filters={sampleFilters} // dateFrom and dateTo are empty
        onFilterChange={vi.fn()}
        onSelectMessage={vi.fn()}
        loading={false}
        onRefresh={vi.fn()}
      />
    );

    const exportPeriodSelect = screen.getByLabelText("Export Period") as HTMLSelectElement;
    fireEvent.change(exportPeriodSelect, { target: { value: "custom" } });

    const downloadBtn = screen.getByRole("button", { name: /download csv/i });
    expect(downloadBtn).toBeDisabled();
  });

  it("exports using custom dashboard date inputs when both dates are provided", async () => {
    const mockBlob = new Blob(["csv content"], { type: "text/csv" });
    vi.mocked(api.exportStatusLogsCsvApi).mockResolvedValueOnce({
      blob: mockBlob,
      filename: "custom_export.csv",
    });

    const customFilters: MessageFilterState = {
      ...sampleFilters,
      dateFrom: "2026-08-01T00:00:00.000Z",
      dateTo: "2026-08-15T23:59:59.999Z",
    };

    render(
      <MessagesTable
        data={sampleData}
        filters={customFilters}
        onFilterChange={vi.fn()}
        onSelectMessage={vi.fn()}
        loading={false}
        onRefresh={vi.fn()}
      />
    );

    const exportPeriodSelect = screen.getByLabelText("Export Period") as HTMLSelectElement;
    fireEvent.change(exportPeriodSelect, { target: { value: "custom" } });

    const downloadBtn = screen.getByRole("button", { name: /download csv/i });
    expect(downloadBtn).not.toBeDisabled();
    fireEvent.click(downloadBtn);

    await waitFor(() => {
      expect(api.exportStatusLogsCsvApi).toHaveBeenCalledTimes(1);
    });

    const calledFilters = vi.mocked(api.exportStatusLogsCsvApi).mock.calls[0][0];
    expect(calledFilters.dateFrom).toBe("2026-08-01T00:00:00.000Z");
    expect(calledFilters.dateTo).toBe("2026-08-15T23:59:59.999Z");
  });
});
