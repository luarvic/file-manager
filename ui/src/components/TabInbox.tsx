import { Box, Link, Stack } from "@mui/material";
import {
  DataGrid,
  GridColDef,
  GridToolbarColumnsButton,
  GridToolbarContainer,
  GridToolbarDensitySelector,
  GridToolbarExport,
  GridToolbarFilterButton,
} from "@mui/x-data-grid";
import { observer } from "mobx-react-lite";
import { useEffect } from "react";
import { ApprovalRequestTypes } from "../models/ApprovalRequestTypes";
import { IUserFile } from "../models/UserFile";
import { approvalRequestStore } from "../stores/ApprovalRequestStore";
import { Tab, commonStore } from "../stores/CommonStore";
import { DATA_GRID_DEFAULT_PAGE_SIZE } from "../stores/Constants";
import { getHumanReadableRelativeDate } from "../utils/Converters";
import { downloadUserFile } from "../utils/Downloaders";
import { ApprovalRequestActions } from "./ApprovalRequestActions";
import ApprovalRequestReviewDialog from "./ApprovalRequestReviewDialog";
import Tabs from "./Tabs";

// Data grid with incoming approval requests.
const TabInbox = () => {
  const { setCurrentTab } = commonStore;
  const { approvalRequests, clearApprovalRequests, loadApprovalRequests } =
    approvalRequestStore;

  useEffect(() => {
    setCurrentTab(Tab.Inbox);
    clearApprovalRequests();
    loadApprovalRequests(ApprovalRequestTypes.Inbox);
  }, []);

  const customToolbar = () => {
    return (
      <GridToolbarContainer>
        <GridToolbarColumnsButton />
        <GridToolbarFilterButton />
        <GridToolbarDensitySelector />
        <GridToolbarExport />
      </GridToolbarContainer>
    );
  };

  const columns: GridColDef[] = [
    {
      field: "sentDate",
      headerName: "Received",
      flex: 2,
      valueFormatter: (params) => getHumanReadableRelativeDate(params.value),
    },
    {
      field: "approveByDate",
      headerName: "Approve by",
      flex: 3,
      valueFormatter: (params) => (params.value as Date).toLocaleString(),
    },
    {
      field: "author",
      headerName: "Requester",
      flex: 5,
      valueGetter: (params) => (params.value as string).toLowerCase(),
    },
    {
      field: "userFiles",
      headerName: "Files",
      flex: 5,
      valueGetter: (params) =>
        params.value.map((userFile: IUserFile) => userFile.name).join(", "),
      renderCell: (params) => {
        return (
          <Stack>
            {params.row.userFiles.map((userFile: IUserFile) => {
              return (
                <Link
                  component="button"
                  onClick={() => downloadUserFile(userFile)}
                >
                  {userFile.name}
                </Link>
              );
            })}
          </Stack>
        );
      },
    },
    {
      field: "comment",
      headerName: "Comment",
      flex: 10,
      renderCell: (params) => {
        return (
          <Stack>
            {(params.value.split(/\r?\n/) as string[]).map((line) => (
              <Box>{line}</Box>
            ))}
          </Stack>
        );
      },
    },
    {
      field: "action",
      headerName: "Action",
      flex: 1,
      renderCell: (params) => {
        return <ApprovalRequestActions approvalRequest={params.row} />;
      },
    },
  ];

  return (
    <Box sx={{ display: "flex", pt: 2 }}>
      <Tabs />
      <Box sx={{ width: "100%", overflow: "hidden", pr: 2 }}>
        <DataGrid
          className="DataGridDefault"
          rows={approvalRequests}
          columns={columns}
          initialState={{
            pagination: {
              paginationModel: {
                pageSize: DATA_GRID_DEFAULT_PAGE_SIZE,
              },
            },
          }}
          pageSizeOptions={[DATA_GRID_DEFAULT_PAGE_SIZE]}
          disableRowSelectionOnClick
          slots={{
            toolbar: customToolbar,
          }}
          slotProps={{
            columnsPanel: {
              disableHideAllButton: true,
              disableShowAllButton: true,
            },
          }}
          getRowHeight={() => "auto"}
          sx={{
            "&.MuiDataGrid-root--densityCompact .MuiDataGrid-cell": { py: 0.5 },
            "&.MuiDataGrid-root--densityStandard .MuiDataGrid-cell": { py: 1 },
            "&.MuiDataGrid-root--densityComfortable .MuiDataGrid-cell": {
              py: 1.5,
            },
          }}
          autoHeight
        />
      </Box>
      <ApprovalRequestReviewDialog />
    </Box>
  );
};

export default observer(TabInbox);
