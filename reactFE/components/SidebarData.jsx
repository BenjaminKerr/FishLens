import React from "react";
import * as FaIcons from "react-icons/fa";
import * as AiIcons from "react-icons/ai";
import * as IoIcons from "react-icons/io";
import * as RiIcons from "react-icons/ri";

export const SidebarData = [
  {
    title: "Home",
    path: "/",
    iconClosed: <RiIcons.RiArrowDownSFill />,
    iconOpened: <RiIcons.RiArrowUpSFill />,
    
    subNav: [
        {
            title: "Dashboard",
            path: "/dashboard",
            icon: <AiIcons.AiFillDashboard />
        },
        {
            title: "Analytics",
            path: "/analytics",
            icon: <IoIcons.IoIosAnalytics />
        },
    ],
},
{
    title: "Settings",
    path: "/settings",
    icon: <IoIcons.IoMdSettings />,
    iconClosed: <RiIcons.RiArrowDownSFill />,
    iconOpened: <RiIcons.RiArrowUpSFill />,
}
];
